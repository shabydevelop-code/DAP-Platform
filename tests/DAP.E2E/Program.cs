using System.Diagnostics;

if (args.Length == 0 || !args[0].Equals("--platform", StringComparison.OrdinalIgnoreCase)
    || args.Length < 2)
    throw new ArgumentException("Usage: --platform web|windows --guide <GuideId> --manual|--hybrid");

var platform = args[1].ToLowerInvariant();
var project = platform switch
{
    "web" => "DAP.TestCRM.Web.E2E",
    "windows" => "DAP.TestCRM.Windows.E2E",
    _ => throw new ArgumentException("Unsupported platform. Use web or windows.")
};

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var projectFile = Path.Combine(repositoryRoot, "tests", project, project + ".csproj");
var forwarded = args.Skip(2).ToArray();
var command = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false,
    WorkingDirectory = repositoryRoot
};
command.ArgumentList.Add("run");
command.ArgumentList.Add("--project");
command.ArgumentList.Add(projectFile);
command.ArgumentList.Add("--");
foreach (var argument in forwarded)
    command.ArgumentList.Add(argument);

using var child = Process.Start(command) ?? throw new InvalidOperationException("Unable to start E2E runner.");
using var cleanup = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cleanup.Cancel();
    DAPProcessCleanup(child);
};
Console.CancelKeyPress += cancel;
try
{
    await child.WaitForExitAsync(cleanup.Token);
    Environment.ExitCode = child.ExitCode;
}
catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
{
    Environment.ExitCode = 130;
}
finally
{
    Console.CancelKeyPress -= cancel;
    DAPProcessCleanup(child);
}

static void DAPProcessCleanup(Process child)
{
    try
    {
        if (!child.HasExited)
            child.Kill(entireProcessTree: true);
    }
    catch (InvalidOperationException) { }
    catch (System.ComponentModel.Win32Exception) { }
}

static string FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "DAP.Core"))
            && Directory.Exists(Path.Combine(directory.FullName, "tests")))
            return directory.FullName;
    throw new DirectoryNotFoundException("DAP repository root was not found.");
}
