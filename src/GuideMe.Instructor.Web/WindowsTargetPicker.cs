using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GuideMe.Instructor.Web;

// Windows counterpart of the Web element picker: non-activating, mouse-transparent
// highlight overlay and click-to-select. UIA work stays on its own STA thread.
internal sealed class WindowsTargetPicker : IDisposable
{
    internal sealed record PickerStatus(bool Picking, bool Picked,
        WindowsTargetInspector.Inspection? Selection, string? Error);
    private readonly object gate = new();
    private Thread? thread;
    private Dispatcher? dispatcher;
    private int processId;
    private bool picking;
    private WindowsTargetInspector.Inspection? selection;
    private string? error;
    private Window? overlay;
    private Border? outline;
    private DispatcherTimer? timer;
    private HookProc? hookDelegate;
    private nint hook;
    private HookProc? keyboardHookDelegate;
    private nint keyboardHook;
    private AutomationElement? hovered;
    private bool disposed;
    private nint returnWindow;
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    internal static nint CaptureInstructorWindow() => GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public Point Point; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }
    private delegate nint HookProc(int code, nuint message, nint data);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint handle, int code, nuint message, nint data);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    internal PickerStatus Status()
    {
        lock (gate) return new PickerStatus(picking, selection is not null, selection, error);
    }

    internal PickerStatus Start(int pid, nint instructorWindow)
    {
        if (pid <= 0) throw new ArgumentException("לא נמצא תהליך יעד תקין.");
        Stop();
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WindowsTargetPicker));
            processId = pid;
            returnWindow = instructorWindow;
            selection = null;
            error = null;
            picking = true;
        }
        thread = new Thread(Run) { IsBackground = true, Name = "GuideMe Windows UIA Picker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return Status();
    }

    internal PickerStatus Stop()
    {
        Dispatcher? toStop;
        lock (gate) { picking = false; toStop = dispatcher; }
        if (toStop is not null)
            try { toStop.BeginInvokeShutdown(DispatcherPriority.Normal); }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) { }
        return Status();
    }

    private void Run()
    {
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            var canvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
            outline = new Border
            {
                // Two-tone outline matches Web picking and remains visible on blue backgrounds.
                BorderBrush = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Child = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(255, 223, 0)),
                    BorderThickness = new Thickness(3),
                    CornerRadius = new CornerRadius(2),
                    Background = Brushes.Transparent,
                    IsHitTestVisible = false
                },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            canvas.Children.Add(outline);
            var left = GetSystemMetrics(76);
            var top = GetSystemMetrics(77);
            var width = GetSystemMetrics(78);
            var height = GetSystemMetrics(79);
            // WPF coordinates are DIPs. Convert from physical pixels below.
            overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Left = left, Top = top, Width = width, Height = height,
                Content = canvas
            };
            overlay.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(overlay).Handle;
                const long transparent = 0x20, toolWindow = 0x80, noActivate = 0x08000000;
                SetWindowLongPtr(hwnd, -20, (nint)(GetWindowLongPtr(hwnd, -20).ToInt64() |
                    transparent | toolWindow | noActivate));
            };
            overlay.Show();
            var source = PresentationSource.FromVisual(overlay);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var origin = transform.Transform(new System.Windows.Point(left, top));
            var corner = transform.Transform(new System.Windows.Point(left + width, top + height));
            overlay.Left = origin.X;
            overlay.Top = origin.Y;
            overlay.Width = corner.X - origin.X;
            overlay.Height = corner.Y - origin.Y;
            hookDelegate = MouseHook;
            hook = SetWindowsHookEx(14, hookDelegate, 0, 0);
            if (hook == 0) throw new InvalidOperationException("לא ניתן להפעיל בחירת רכיב בעכבר.");
            keyboardHookDelegate = KeyboardHook;
            keyboardHook = SetWindowsHookEx(13, keyboardHookDelegate, 0, 0);
            if (keyboardHook == 0) throw new InvalidOperationException("לא ניתן להפעיל קיצור מקלדת לבחירת רכיב.");
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
            timer.Tick += (_, _) => UpdateHover(transform, origin);
            timer.Start();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            lock (gate) { error = ex.Message; picking = false; }
        }
        finally
        {
            timer?.Stop();
            if (keyboardHook != 0) { UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
            if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; }
            overlay?.Close();
            lock (gate) { picking = false; dispatcher = null; }
        }
    }

    private void UpdateHover(Matrix transform, System.Windows.Point origin)
    {
        if (!GetCursorPos(out var cursor)) return;
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(cursor.X, cursor.Y));
            if (element is null || element.Current.ProcessId != processId)
            {
                hovered = null;
                outline!.Visibility = Visibility.Collapsed;
                return;
            }
            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 2 || bounds.Height < 2)
            {
                hovered = null;
                outline!.Visibility = Visibility.Collapsed;
                return;
            }
            hovered = element;
            var a = transform.Transform(new System.Windows.Point(bounds.Left, bounds.Top));
            var b = transform.Transform(new System.Windows.Point(bounds.Right, bounds.Bottom));
            Canvas.SetLeft(outline!, a.X - origin.X);
            Canvas.SetTop(outline!, a.Y - origin.Y);
            outline!.Width = b.X - a.X;
            outline.Height = b.Y - a.Y;
            outline.Visibility = Visibility.Visible;
        }
        catch (ElementNotAvailableException) { hovered = null; outline!.Visibility = Visibility.Collapsed; }
        catch (COMException) { hovered = null; outline!.Visibility = Visibility.Collapsed; }
    }

    // The keyboard shortcut selects the hovered element without activating it.
    // Mouse clicks are never intercepted: normal interaction with the app is preserved.
    private nint KeyboardHook(int code, nuint message, nint data)
    {
        if (code >= 0 && (message == 0x100 || message == 0x104))
        {
            var key = Marshal.PtrToStructure<KeyboardHookData>(data);
            if (key.VkCode == 0x1B) // Escape cancels the active Windows picker.
            {
                lock (gate) picking = false;
                dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
                return 1;
            }
            if (key.VkCode == 0x20 &&
                (GetAsyncKeyState(0x11) & 0x8000) != 0 &&
                (GetAsyncKeyState(0x10) & 0x8000) != 0 &&
                hovered is not null)
            {
                SelectHovered();
                return 1;
            }
        }
        return CallNextHookEx(keyboardHook, code, message, data);
    }

    private nint MouseHook(int code, nuint message, nint data) =>
        CallNextHookEx(hook, code, message, data);

    private void SelectHovered()
    {
        try
        {
            var result = WindowsTargetInspector.Inspect(processId, false);
            if (result.Error is not null)
            {
                lock (gate) { selection = null; error = result.Error; picking = false; }
                dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
                return;
            }
            lock (gate) { selection = result; error = null; picking = false; }
            // Return to the authoring window after a successful pick, not on Escape.
            if (returnWindow != 0)
            {
                try { GuideMe.Runtime.Windows.Targets.WindowsWindowActivation.Activate(returnWindow); }
                catch (InvalidOperationException) { /* Selection remains saved if the authoring window closed. */ }
            }
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException)
        {
            lock (gate) error = ex.Message;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (gate) disposed = true;
    }
}
