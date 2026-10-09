using System.Diagnostics;
using System.Net.Http;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var serverProject = Path.Combine(root, "demos", "Shared", "Server", "DAP.TestCRM.Server.csproj");
var windowsProject = Path.Combine(root, "demos", "Windows", "App", "DAP.TestCRM.Windows.csproj");

async Task BuildAsync(string project)
{
    using var build = Process.Start(new ProcessStartInfo("dotnet")
    {
        ArgumentList = { "build", project, "--nologo", "--verbosity", "minimal" },
        WorkingDirectory = root,
        UseShellExecute = false
    }) ?? throw new InvalidOperationException("Build process could not start.");
    await build.WaitForExitAsync();
    if (build.ExitCode != 0)
        throw new InvalidOperationException($"Build failed: {project} ({build.ExitCode}).");
}

Process Start(string file, string workingDirectory, params string[] arguments)
{
    var info = new ProcessStartInfo(file) { WorkingDirectory = workingDirectory, UseShellExecute = false };
    foreach (var argument in arguments)
        info.ArgumentList.Add(argument);
    return Process.Start(info) ?? throw new InvalidOperationException($"Cannot start {file}.");
}

void Stop(Process? process)
{
    if (process is null) return;
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

await BuildAsync(serverProject);
await BuildAsync(windowsProject);

Process? server = null;
Process? windows = null;
try
{
    var serverDll = Path.Combine(Path.GetDirectoryName(serverProject)!, "bin", "Debug", "net8.0", "DAP.TestCRM.Server.dll");
    var windowsExe = Path.Combine(Path.GetDirectoryName(windowsProject)!, "bin", "Debug", "net8.0-windows", "DAP.TestCRM.Windows.exe");
    server = Start("dotnet", Path.GetDirectoryName(serverDll)!, serverDll, "--urls", "http://localhost:5201");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    var deadline = DateTime.UtcNow.AddSeconds(5);
    var ready = false;
    while (DateTime.UtcNow < deadline)
    {
        if (server.HasExited)
            throw new InvalidOperationException($"TestCRM server exited: {server.ExitCode}.");
        try
        {
            using var response = await http.GetAsync("http://localhost:5201/api/customers");
            if (response.IsSuccessStatusCode) { ready = true; break; }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        await Task.Delay(100);
    }
    if (!ready)
        throw new TimeoutException("TestCRM server did not become ready within five seconds.");

    windows = Start(windowsExe, Path.GetDirectoryName(windowsExe)!);
    Console.WriteLine("TestCRM Windows is running independently of DAP.");
    Console.WriteLine("Start DAP.exe --guide testcrm-windows-canonical-workflow in another terminal.");
    Console.WriteLine("Press Ctrl+C to stop the TestCRM host.");

    var serverExit = server.WaitForExitAsync();
    var windowsExit = windows.WaitForExitAsync();
    var completed = await Task.WhenAny(serverExit, windowsExit);
    if (completed == windowsExit)
    {
        await windowsExit;
        if (windows.ExitCode != 0)
            throw new InvalidOperationException($"TestCRM Windows exited with code {windows.ExitCode}.");
        Console.WriteLine("TestCRM Windows closed normally. Stopping the host.");
    }
    else
    {
        await serverExit;
        throw new InvalidOperationException($"TestCRM server exited with code {server.ExitCode}.");
    }
}
finally
{
    Stop(windows);
    Stop(server);
    windows?.Dispose();
    server?.Dispose();
}
