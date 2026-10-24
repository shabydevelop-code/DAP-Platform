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
            if (!IsWindowVisible(handle) || GetWindow(handle, 4) != 0 || IsCloaked(handle) || IsToolWindow(handle)) return true;
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
                    process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                    IsShellInfrastructure(process.ProcessName))
                    return true;
                windows.Add(new WindowCandidate(process.ProcessName, title.ToString(), process.Id));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        return windows.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // Shell hosts are not stable application identities.
    private static bool IsShellInfrastructure(string name) =>
        name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);

    private static bool IsCloaked(nint hwnd)
    {
        try { return DwmGetWindowAttribute(hwnd, 14, out int value, 4) == 0 && value != 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static bool IsToolWindow(nint hwnd) =>
        (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80L) != 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

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
