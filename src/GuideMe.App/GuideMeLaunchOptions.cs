using System.Windows;
using GuideMe.Core.Localization;

namespace GuideMe.App;

public enum GuideMeLaunchMode
{
    InfrastructureCheck,
    ValidateV5,
    Learner
}

public sealed record GuideMeLaunchOptions(
    GuideMeLaunchMode Mode,
    string? GuideId,
    string? WindowAutomationId,
    string? V5DatabasePath = null)
{
    public static GuideMeLaunchOptions? Parse(string[] args, IUiTextProvider texts)
    {
        if (args.Length == 4 && args[0] == "--validate-v5" && !string.IsNullOrWhiteSpace(args[1])
            && args[2] == "--guide" && !string.IsNullOrWhiteSpace(args[3]))
            return new(GuideMeLaunchMode.ValidateV5, args[3], null, V5DatabasePath: args[1]);

        if (args.Length == 1 && args[0] == "--check")
            return new(GuideMeLaunchMode.InfrastructureCheck, null, null);

        if (args.Length >= 2
            && args[0] == "--guide"
            && !string.IsNullOrWhiteSpace(args[1]))
        {
            string? windowAutomationId = null;
            string? v5DatabasePath = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--v5-db" && i + 1 < args.Length
                    && v5DatabasePath is null && !string.IsNullOrWhiteSpace(args[i + 1]))
                    v5DatabasePath = args[++i];
                else if (args[i] == "--window-automation-id" && i + 1 < args.Length
                    && !string.IsNullOrWhiteSpace(args[i + 1]))
                    windowAutomationId = args[++i];
                else
                    return Usage(texts);
            }

            return new(
                GuideMeLaunchMode.Learner,
                args[1],
                windowAutomationId,
                v5DatabasePath);
        }

        return Usage(texts);
    }

    private static GuideMeLaunchOptions? Usage(IUiTextProvider texts)
    {
        System.Windows.MessageBox.Show(
            texts.Get("App.Usage"),
            texts.Get("App.WindowTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }
}
