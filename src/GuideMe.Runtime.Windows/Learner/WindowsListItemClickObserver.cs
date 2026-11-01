using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;

namespace GuideMe.Runtime.Windows.Learner;

// Observes a real mouse press/release on a specific UIA list item.
// Unlike disappearance, an observed click is an explicit user action.
internal sealed class WindowsListItemClickObserver : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private readonly AutomationElement target;
    private readonly Action onClick;
    private readonly Action<string> diagnose;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private Dispatcher? dispatcher;
    private HookProc? callback;
    private nint hook;
    private Point? pressedAt;
    private Exception? startupError;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public Point Position;
        public uint MouseDataValue;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
    private delegate nint HookProc(int code, nuint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc procedure, nint module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint handle, int code, nuint message, nint data);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    public WindowsListItemClickObserver(AutomationElement target, Action onClick, Action<string> diagnose)
    {
        this.target = target;
        this.onClick = onClick;
        this.diagnose = diagnose;
        thread = new Thread(Run) { IsBackground = true, Name = "GuideMe Windows ListItem Click Observer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("Windows list item mouse observer did not start.");
        if (startupError is not null)
            throw new InvalidOperationException("Windows list item mouse observer could not start.", startupError);
    }

    private void Run()
    {
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            callback = MouseHook;
            hook = SetWindowsHookEx(WhMouseLl, callback, 0, 0);
            if (hook == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            startupError = ex;
            ready.Set();
        }
        finally
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
            hook = 0;
        }
    }

    private nint MouseHook(int code, nuint message, nint data)
    {
        if (code >= 0 && (message == WmLButtonDown || message == WmLButtonUp))
        {
            var position = Marshal.PtrToStructure<MouseData>(data).Position;
            if (message == WmLButtonDown)
            {
                pressedAt = IsTargetAt(position) ? position : null;
            }
            else
            {
                var start = pressedAt;
                pressedAt = null;
                if (start is not null &&
                    Math.Abs(start.Value.X - position.X) <= 8 &&
                    Math.Abs(start.Value.Y - position.Y) <= 8)
                {
                    diagnose("list-item-mouse-click");
                    onClick();
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private bool IsTargetAt(Point point)
    {
        try
        {
            var hit = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            for (var current = hit; current is not null;
                 current = TreeWalker.RawViewWalker.GetParent(current))
                if (Automation.Compare(current, target))
                    return true;
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }

        // Classic Win32 menu headers are not always returned by UIA hit testing.
        // Accept only a press within the captured MenuItem's own bounds, on a
        // window belonging to that same process. Never infer a click from a
        // popup appearing or from a target disappearing.
        try
        {
            if (target.Current.ControlType != ControlType.MenuItem ||
                !target.Current.BoundingRectangle.Contains(point.X, point.Y))
                return false;
            var hwnd = WindowFromPoint(point);
            if (hwnd == 0) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            return pid == target.Current.ProcessId;
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        return false;
    }

    public void Dispose()
    {
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        if (Thread.CurrentThread != thread && thread.IsAlive)
            thread.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }
}
