using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Net.Http;

const string webUrl = "http://localhost:5200";
const string backendUrl = "http://localhost:5201";

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
var backendProject = Path.Combine(repoRoot, "demos", "Shared", "Server", "DAP.TestCRM.Server.csproj");
var webProject = Path.Combine(repoRoot, "demos", "Web", "App", "DAP.TestCRM.Web.csproj");

if (!File.Exists(backendProject))
    throw new FileNotFoundException("TestCRM Server project was not found.", backendProject);
if (!File.Exists(webProject))
    throw new FileNotFoundException("TestCRM Web project was not found.", webProject);

async Task BuildProjectAsync(string project)
{
    using var build = Process.Start(new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build \"{project}\" --nologo --verbosity minimal",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }) ?? throw new InvalidOperationException($"Could not build {Path.GetFileNameWithoutExtension(project)}.");

    var stdout = build.StandardOutput.ReadToEndAsync();
    var stderr = build.StandardError.ReadToEndAsync();
    await build.WaitForExitAsync();
    if (build.ExitCode != 0)
        throw new InvalidOperationException(
            $"Build failed for {Path.GetFileNameWithoutExtension(project)}.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{await stdout}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{await stderr}");
}

Process StartBuiltProject(string project, string url, IReadOnlyDictionary<string,string>? environment = null)
{
    var projectDirectory = Path.GetDirectoryName(project)!;
    var dll = Path.Combine(
        projectDirectory,
        "bin",
        "Debug",
        "net8.0",
        Path.GetFileNameWithoutExtension(project) + ".dll");

    if (!File.Exists(dll))
        throw new FileNotFoundException("Built TestCRM assembly was not found.", dll);

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"\"{dll}\" --urls {url}",
        WorkingDirectory = projectDirectory,
        UseShellExecute = false
    };
    if (environment is not null)
        foreach (var pair in environment)
            psi.Environment[pair.Key] = pair.Value;

    return Process.Start(psi)
        ?? throw new InvalidOperationException($"Could not start {Path.GetFileNameWithoutExtension(project)}.");
}

await BuildProjectAsync(backendProject);
await BuildProjectAsync(webProject);

var backend = StartBuiltProject(backendProject, backendUrl);
var web = StartBuiltProject(
    webProject,
    webUrl,
    new Dictionary<string,string> { ["TestCrmBackendUrl"] = backendUrl });

void Stop(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
    catch (InvalidOperationException) { }
    catch (System.ComponentModel.Win32Exception) { }
}

var stopping = 0;
ConsoleCancelEventHandler cancel = (_, e) =>
{
    e.Cancel = true;
    Interlocked.Exchange(ref stopping, 1);
    Stop(web);
    Stop(backend);
};
Console.CancelKeyPress += cancel;

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    var deadline = DateTime.UtcNow.AddSeconds(5);
    var ready = false;
    while (DateTime.UtcNow < deadline)
    {
        if (backend.HasExited)
            throw new InvalidOperationException($"TestCRM Backend exited with code {backend.ExitCode}.");
        if (web.HasExited)
            throw new InvalidOperationException($"TestCRM Web exited with code {web.ExitCode}.");

        try
        {
            using var response = await http.GetAsync(webUrl);
            if ((int)response.StatusCode < 500)
            {
                ready = true;
                break;
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        await Task.Delay(100);
    }

    if (!ready)
        throw new TimeoutException("TestCRM Web did not become ready within 5 seconds.");

    // Open a normal Chrome window using the user's existing browser profile.
    // A fresh isolated profile would not have the DAP extension installed.
    if (!OperatingSystem.IsWindows())
        throw new PlatformNotSupportedException("TestCRM Web Host requires Windows to launch Chrome.");

    var existingChromeWindows = GetChromeWindows();
    using (var launcher = Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        ArgumentList = { "/c", "start", "", "chrome", "--new-window", webUrl },
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException("Could not request Chrome launch."))
    {
        await launcher.WaitForExitAsync();
        if (launcher.ExitCode != 0)
            throw new InvalidOperationException("Chrome launch failed.");
    }

    // Chrome may reuse an existing process; its PID is not the lifetime of this window.
    // Track only the newly created top-level Chrome window, never the whole browser.
    nint chromeWindow = 0;
    var windowDeadline = DateTime.UtcNow.AddSeconds(5);
    while (DateTime.UtcNow < windowDeadline)
    {
        var newWindows = GetChromeWindows().Except(existingChromeWindows).ToArray();
        if (newWindows.Length > 1)
            throw new InvalidOperationException("Multiple new Chrome windows appeared; cannot identify the TestCRM window safely.");
        if (newWindows.Length == 1)
        {
            chromeWindow = newWindows[0];
            break;
        }
        await Task.Delay(100);
    }
    if (chromeWindow == 0)
        throw new InvalidOperationException("Could not identify the newly opened TestCRM Chrome window.");

    Console.WriteLine();
    Console.WriteLine($"TestCRM is running independently at {webUrl}");
    Console.WriteLine("Leave this terminal open. Start DAP Learner from a separate terminal.");
    Console.WriteLine("Close the TestCRM Chrome window or press Ctrl+C to stop the demo servers.");

    var backendExit = backend.WaitForExitAsync();
    var webExit = web.WaitForExitAsync();
    while (Volatile.Read(ref stopping) == 0 && !backendExit.IsCompleted && !webExit.IsCompleted && IsWindow(chromeWindow))
        await Task.Delay(200);
    if (Volatile.Read(ref stopping) != 0 || !IsWindow(chromeWindow))
    {
        Console.WriteLine("TestCRM host stopped normally.");
    }
    else if (backend.HasExited)
        throw new InvalidOperationException($"TestCRM Backend exited with code {backend.ExitCode}.");
    else
        throw new InvalidOperationException($"TestCRM Web exited with code {web.ExitCode}.");
}
finally
{
    Console.CancelKeyPress -= cancel;
    Stop(web);
    Stop(backend);
    web.Dispose();
    backend.Dispose();
}

static HashSet<nint> GetChromeWindows()
{
    var windows = new HashSet<nint>();
    EnumWindows((handle, _) =>
    {
        var className = new StringBuilder(128);
        if (GetClassName(handle, className, className.Capacity) == 0 ||
            !string.Equals(className.ToString(), "Chrome_WidgetWin_1", StringComparison.Ordinal) ||
            !IsWindowVisible(handle))
            return true;

        GetWindowThreadProcessId(handle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase))
                windows.Add(handle);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return true;
    }, nint.Zero);
    return windows;
}

delegate bool EnumWindowsCallback(nint window, nint parameter);

[DllImport("user32.dll")]
static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int GetClassName(nint window, StringBuilder className, int maxCount);

[DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(nint window, out uint processId);

[DllImport("user32.dll")]
static extern bool IsWindowVisible(nint window);

[DllImport("user32.dll")]
static extern bool IsWindow(nint window);
