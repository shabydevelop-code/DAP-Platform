using DAP.E2E.Platforms;

if (args.Length < 2 || !args[0].Equals("--platform", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Usage: --platform web|windows --guide <GuideId> --manual|--hybrid");

var platform = args[1].ToLowerInvariant();
var scenarioArgs = args.Skip(2).ToArray();
switch (platform)
{
    case "web":
        _ = DAP.Testing.E2eRunOptions.Parse(scenarioArgs, "Web");
        await WebScenario.RunAsync(scenarioArgs);
        break;
    case "windows":
        _ = DAP.Testing.E2eRunOptions.Parse(scenarioArgs, "Windows");
        await WindowsScenario.RunAsync(scenarioArgs);
        break;
    default:
        throw new ArgumentException("Unsupported platform. Use web or windows.");
}
