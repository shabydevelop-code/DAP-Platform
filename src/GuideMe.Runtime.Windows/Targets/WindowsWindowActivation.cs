using System.Runtime.InteropServices;

namespace GuideMe.Runtime.Windows.Targets;

// Shared foreground activation for production Learner (Windows and mixed guides)
// and the Windows Instructor mapper. Never inject keyboard or mouse input.
public static class WindowsWindowActivation
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint from, uint to, bool attach);

    public static bool Activate(nint target)
    {
        if (!OperatingSystem.IsWindows() || target == 0 || !IsWindow(target))
            throw new InvalidOperationException("Target Windows window is not available.");
        if (GetForegroundWindow() == target) return true;
        if (IsIconic(target)) ShowWindow(target, 9);

        if (SetForegroundWindow(target) && GetForegroundWindow() == target)
            return true;

        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == target) return GetForegroundWindow() == target;

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(target, out _);
        var callerThread = GetCurrentThreadId();
        if (foregroundThread == 0 || targetThread == 0) return false;

        // Same input-queue strategy as the proven mixed-guide Learner:
        // join the caller AND the target to the active foreground queue.
        var callerAttached = foregroundThread != callerThread &&
            AttachThreadInput(callerThread, foregroundThread, true);
        var targetAttached = foregroundThread != targetThread &&
            AttachThreadInput(targetThread, foregroundThread, true);
        try
        {
            if ((foregroundThread != callerThread && !callerAttached) ||
                (foregroundThread != targetThread && !targetAttached))
                return false;
            SetForegroundWindow(target);
            return GetForegroundWindow() == target;
        }
        finally
        {
            if (targetAttached) AttachThreadInput(targetThread, foregroundThread, false);
            if (callerAttached) AttachThreadInput(callerThread, foregroundThread, false);
        }
    }
}
