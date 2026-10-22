using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace GuideMe.Instructor.Web;

// The UI connection is the lifetime owner, not the Chrome process (which may
// already exist and serves unrelated tabs). We never terminate Chrome.
internal sealed class InstructorWindowLifetime
{
    private readonly IHostApplicationLifetime _application;
    private readonly object _gate = new();
    private int _connections;
    private int _revision;

    internal InstructorWindowLifetime(IHostApplicationLifetime application) => _application = application;

    internal void Connected()
    {
        lock (_gate)
        {
            _connections++;
            _revision++;
        }
    }

    internal void Disconnected()
    {
        int revision;
        lock (_gate)
        {
            if (_connections == 0) return;
            _connections--;
            revision = ++_revision;
            if (_connections != 0) return;
        }

        // Reloads close the old socket before establishing the next one.
        // Never exit immediately; still shut down promptly on window close.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            lock (_gate)
            {
                if (_connections == 0 && _revision == revision)
                    _application.StopApplication();
            }
        });
    }

    internal static string? FindChromeExecutable()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var key in new[]
        {
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
        })
        {
            var registered = Registry.GetValue(key, "", null) as string;
            if (!string.IsNullOrWhiteSpace(registered) && File.Exists(registered))
                return registered;
        }
        foreach (var baseDir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
        {
            if (string.IsNullOrEmpty(baseDir)) continue;
            var path = Path.Combine(baseDir, "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
