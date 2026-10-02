using System.Windows;

namespace DAP.App;

public enum DapLaunchMode
{
    InfrastructureCheck,
    LearnerWeb,
    LearnerWindows
}

public sealed record DapLaunchOptions(
    DapLaunchMode Mode,
    string? GuideId,
    string? CdpEndpoint,
    string? PageUrlContains,
    string? WindowAutomationId,
    bool ShowCompletion,
    int? StartStep)
{
    public static DapLaunchOptions? Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--check")
            return new(DapLaunchMode.InfrastructureCheck, null, null, null, null, false, null);

        if (args.Length >= 2 && args[0] == "--learner-web" && !string.IsNullOrWhiteSpace(args[1]))
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
                return new(DapLaunchMode.LearnerWeb, args[1], cdp, pageUrlContains, null, showCompletion, startStep);

            return Usage();
        }

        if (args.Length >= 2 && args[0] == "--learner-windows" && !string.IsNullOrWhiteSpace(args[1]))
        {
            string? windowAutomationId = null;
            var showCompletion = false;
            int? startStep = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--window-automation-id" && i + 1 < args.Length)
                    windowAutomationId = args[++i];
                else if (args[i] == "--show-completion")
                    showCompletion = true;
                else if (args[i] == "--start-step" && i + 1 < args.Length
                         && int.TryParse(args[++i], out var parsedStartStep) && parsedStartStep > 0)
                    startStep = parsedStartStep;
                else
                    return Usage();
            }

            if (!string.IsNullOrWhiteSpace(windowAutomationId))
                return new(DapLaunchMode.LearnerWindows, args[1], null, null, windowAutomationId, showCompletion, startStep);

            return Usage();
        }

        return Usage();
    }

    private static DapLaunchOptions? Usage()
    {
        MessageBox.Show(
            "Usage:\n" +
            "DAP.exe --check\n" +
            "DAP.exe --learner-web <guide-key> --cdp <endpoint> [--page-url-contains <text>] [--show-completion] [--start-step <order>]\n" +
            "DAP.exe --learner-windows <guide-key> --window-automation-id <id> [--show-completion] [--start-step <order>]",
            "DAP",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }
}
