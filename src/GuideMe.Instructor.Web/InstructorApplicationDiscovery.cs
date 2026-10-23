using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GuideMe.Instructor.Web;

// Lists visible, top-level Windows applications. Discovery never edits or
// activates the target window; the user explicitly chooses an entry.
internal static class InstructorApplicationDiscovery
{
    internal sealed record WindowCandidate(string ProcessName, string Title, int ProcessId);

    internal static IReadOnlyList<WindowCandidate> Windows()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<WindowCandidate>();
        var windows = new List<WindowCandidate>();
        var ownSession = Process.GetCurrentProcess().SessionId;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, 4) != 0) return true; // owned/hidden windows
            var size = GetWindowTextLength(handle);
            if (size is < 1 or > 300) return true;
            var title = new StringBuilder(size + 1);
            GetWindowText(handle, title, title.Capacity);
            GetWindowThreadProcessId(handle, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (process.SessionId != ownSession ||
                    process.Id == Environment.ProcessId ||
                    process.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    return true;
                windows.Add(new WindowCandidate(process.ProcessName, title.ToString(), process.Id));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        return windows.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private delegate bool EnumWindowProc(nint handle, nint state);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowProc callback, nint state);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);
}
