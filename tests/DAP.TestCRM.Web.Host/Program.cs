using System.Diagnostics;
using System.Net.Http;

const string webUrl = "http://localhost:5200";
const string backendUrl = "http://localhost:5201";

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var backendProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Server", "DAP.TestCRM.Server.csproj");
var webProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Web", "DAP.TestCRM.Web.csproj");

if (!File.Exists(backendProject))
    throw new FileNotFoundException("TestCRM Server project was not found.", backendProject);
if (!File.Exists(webProject))
    throw new FileNotFoundException("TestCRM Web project was not found.", webProject);

Process StartProject(string project, string url, IReadOnlyDictionary<string,string>? environment = null)
{
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"run --no-launch-profile --project \"{project}\" --urls {url}",
        WorkingDirectory = repoRoot,
        UseShellExecute = false
    };
    if (environment is not null)
        foreach (var pair in environment)
            psi.Environment[pair.Key] = pair.Value;

    return Process.Start(psi)
        ?? throw new InvalidOperationException($"Could not start {Path.GetFileNameWithoutExtension(project)}.");
}

var backend = StartProject(backendProject, backendUrl);
var web = StartProject(
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

ConsoleCancelEventHandler cancel = (_, e) =>
{
    e.Cancel = true;
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

    Console.WriteLine();
    Console.WriteLine($"TestCRM is running independently at {webUrl}");
    Console.WriteLine("Leave this terminal open. Start DAP Learner from a separate terminal.");
    Console.WriteLine("Press Ctrl+C here to stop TestCRM.");

    await Task.WhenAny(backend.WaitForExitAsync(), web.WaitForExitAsync());
    if (backend.HasExited)
        throw new InvalidOperationException($"TestCRM Backend exited with code {backend.ExitCode}.");
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
