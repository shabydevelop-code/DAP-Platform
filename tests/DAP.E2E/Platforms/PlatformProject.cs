namespace DAP.E2E.Platforms;

// Temporary bridge until the platform-specific E2E implementations move into this project.
internal static class PlatformProject
{
    public static string Resolve(string platform) => platform.ToLowerInvariant() switch
    {
        "web" => "DAP.TestCRM.Web.E2E",
        "windows" => "DAP.TestCRM.Windows.E2E",
        _ => throw new ArgumentException("Unsupported platform. Use web or windows.")
    };
}
