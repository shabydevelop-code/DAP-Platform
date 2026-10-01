using System.Windows;

namespace DAP.App;

public enum DapLaunchMode
{
    InfrastructureCheck,
    LearnerWeb
}

public sealed record DapLaunchOptions(
    DapLaunchMode Mode,
    string? GuideId,
    string? CdpEndpoint,
    string? PageUrlContains,
    bool ShowCompletion,
    int? StartStep)
{
    public static DapLaunchOptions? Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--check")
            return new(DapLaunchMode.InfrastructureCheck, null, null, null, false, null);

        if (args.Length >= 4 && args[0] == "--learner-web" && !string.IsNullOrWhiteSpace(args[1]))
        {
            string? cdp = null;
            string? pageUrlContains = null;
            var showCompletion = false;
            int? startStep = null;
            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--cdp" && i + 1 < args.Length)
                    cdp = args[++i];
                else if (args[i] == "--page-url-contains" && i + 1 < args.Length)
                    pageUrlContains = args[++i];
                else if (args[i] == "--show-completion")
                    showCompletion = true;
                else if (args[i] == "--start-step" && i + 1 < args.Length
                         && int.TryParse(args[++i], out var parsedStartStep) && parsedStartStep > 0)
                    startStep = parsedStartStep;
                else
                    return Usage();
            }

            if (!string.IsNullOrWhiteSpace(cdp))
                return new(DapLaunchMode.LearnerWeb, args[1], cdp, pageUrlContains, showCompletion, startStep);
        }

        return Usage();
    }

    private static DapLaunchOptions? Usage()
    {
        MessageBox.Show(
            "Usage:\nDAP.exe --check\nDAP.exe --learner-web <guide-id> --cdp <endpoint> [--page-url-contains <text>] [--show-completion] [--start-step <order>]",
            "DAP",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }
}
