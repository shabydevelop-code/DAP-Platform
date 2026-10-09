using System.IO;
namespace DAP.E2E.Platforms;

// Transitional platform adapters. The scenario implementations still reside in legacy projects.
internal interface IE2ePlatform
{
    string Name { get; }
    string ProjectPath(string repositoryRoot);
}

internal sealed class WebE2ePlatform : IE2ePlatform
{
    public string Name => "web";
    public string ProjectPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "tests", "DAP.TestCRM.Web.E2E", "DAP.TestCRM.Web.E2E.csproj");
}

internal sealed class WindowsE2ePlatform : IE2ePlatform
{
    public string Name => "windows";
    public string ProjectPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "tests", "DAP.TestCRM.Windows.E2E", "DAP.TestCRM.Windows.E2E.csproj");
}

internal static class PlatformProject
{
    public static IE2ePlatform Resolve(string platform) => platform.ToLowerInvariant() switch
    {
        "web" => new WebE2ePlatform(),
        "windows" => new WindowsE2ePlatform(),
        _ => throw new ArgumentException("Unsupported platform. Use web or windows.")
    };
}
