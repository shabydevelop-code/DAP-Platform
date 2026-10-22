using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GuideMe.Instructor.Web;

// One Instructor server and one Chrome app window per Windows session.
// A subsequent launch only raises the existing window; it never starts Kestrel.
internal static class InstructorSingleInstance
{
    private const string WindowTitle = "GuideMe — Instructor";
    private const int SwRestore = 9;

    internal static Mutex? TryAcquire()
    {
        var name = @"Local\GuideMe.Instructor.Web." + Process.GetCurrentProcess().SessionId;
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        return null;
    }

    internal static bool ActivateExisting()
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Chrome may still be opening after the first launch.
        for (var attempt = 0; attempt < 15; attempt++)
        {
            nint target = 0;
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window)) return true;
                var length = GetWindowTextLength(window);
                if (length != WindowTitle.Length) return true;
                var title = new StringBuilder(length + 1);
                _ = GetWindowText(window, title, title.Capacity);
                if (!string.Equals(title.ToString(), WindowTitle, StringComparison.Ordinal))
                    return true;
                GetWindowThreadProcessId(window, out var pid);
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    if (!string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (ArgumentException) { return true; }
                target = window;
                return false;
            }, 0);
            if (target != 0)
            {
                ShowWindow(target, SwRestore);
                return SetForegroundWindow(target);
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private delegate bool WindowEnumerator(nint window, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowEnumerator callback, nint state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
