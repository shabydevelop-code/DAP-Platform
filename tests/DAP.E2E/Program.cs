using DAP.E2E.Platforms;
using DAP.E2E.Runner;

if (args.Length < 2 || !args[0].Equals("--platform", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Usage: --platform web|windows --guide <GuideId> --manual|--hybrid");

var project = PlatformProject.Resolve(args[1]);
_ = DAP.Testing.E2eRunOptions.Parse(args.Skip(2).ToArray(), args[1].Equals("web", StringComparison.OrdinalIgnoreCase) ? "Web" : "Windows");
var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var projectFile = Path.Combine(repositoryRoot, "tests", project, project + ".csproj");
Environment.ExitCode = await E2eRunner.RunAsync(repositoryRoot, projectFile, args.Skip(2).ToArray());

static string FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "DAP.Core"))
            && Directory.Exists(Path.Combine(directory.FullName, "tests")))
            return directory.FullName;
    throw new DirectoryNotFoundException("DAP repository root was not found.");
}
