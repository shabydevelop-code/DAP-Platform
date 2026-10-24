using System.Diagnostics;
using System.Runtime.InteropServices;

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

    internal static bool Activate(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return false;
        using var process = Process.GetProcessById(processId);
        var target = process.MainWindowHandle;
        if (target == 0) return false;

        if (IsIconic(target)) ShowWindowAsync(target, SwRestore);
        if (GetForegroundWindow() == target) return true;

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

        return GetForegroundWindow() == target;
    }
}
