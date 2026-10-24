using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace GuideMe.Instructor.Web;

// Activates the configured Windows application's own main window when the
// author starts element mapping. Windows foreground-lock rules mean that a
// background Kestrel process cannot rely on SetForegroundWindow alone.
internal static class InstructorTargetActivation
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    // INPUT is a tagged union. KEYBDINPUT starts at offset 8 on win-x64.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private static bool RequestForegroundPermission()
    {
        // Windows permits foreground activation following an Alt keystroke.
        // Only pulse Alt when no modifiers are held; never send text or clicks.
        const int vkAlt = 0x12, vkCtrl = 0x11, vkShift = 0x10, vkWin = 0x5B;
        if (GetAsyncKeyState(vkAlt) < 0 || GetAsyncKeyState(vkCtrl) < 0 ||
            GetAsyncKeyState(vkShift) < 0 || GetAsyncKeyState(vkWin) < 0 ||
            GetAsyncKeyState(0x5C) < 0) return false;
        var inputs = new[]
        {
            new NativeInput { Type = 1, Keyboard = new KeyboardInput { Vk = vkAlt } },
            new NativeInput { Type = 1, Keyboard = new KeyboardInput { Vk = vkAlt, Flags = 0x0002 } }
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>()) == inputs.Length;
    }

    internal static bool Activate(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return false;
        using var process = Process.GetProcessById(processId);
        var target = process.MainWindowHandle;
        if (target == 0) return false;

        if (IsIconic(target)) ShowWindowAsync(target, SwRestore);
        if (GetForegroundWindow() == target) return true;

        // Mirror Windows Learner's UI Automation focus strategy instead of
        // relying exclusively on Win32 foreground promotion from Kestrel.
        // Focus the application WINDOW, not an arbitrary input field.
        try
        {
            var window = AutomationElement.FromHandle(target);
            window?.SetFocus();
            if (GetForegroundWindow() == target) return true;
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }

        var foreground = GetForegroundWindow();
        var foregroundThread = foreground != 0
            ? GetWindowThreadProcessId(foreground, out _) : 0;
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(target);
            SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }

        if (GetForegroundWindow() == target) return true;

        // The Instructor runs behind Chrome; attaching input queues alone does
        // not confer permission to take focus under the Windows foreground lock.
        // Retry after a synthetic, non-text Alt tap to unlock foreground activation.
        if (!RequestForegroundPermission()) return false;
        SetForegroundWindow(target);
        return GetForegroundWindow() == target;
    }
}
