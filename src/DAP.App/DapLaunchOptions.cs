using System.Windows;
using DAP.Core.Localization;

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
    string? WindowAutomationId,
    int? StartStep,
    string? ResumeContextPath,
    int? ShowGuidanceFromStep,
    bool HideGuidance)
{
    public static DapLaunchOptions? Parse(string[] args, IUiTextProvider texts)
    {
        if (args.Length == 1 && args[0] == "--check")
            return new(DapLaunchMode.InfrastructureCheck, null, null, null, null, null, false);

        if (args.Length >= 2 && args[0] == "--learner-web" && !string.IsNullOrWhiteSpace(args[1]))
        {
            int? startStep = null;
            string? resumeContextPath = null;
            int? showGuidanceFromStep = null;
            var hideGuidance = false;

            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--start-step" && i + 1 < args.Length
                         && int.TryParse(args[++i], out var parsedStartStep) && parsedStartStep > 0)
                    startStep = parsedStartStep;
                else if (args[i] == "--resume-context-file" && i + 1 < args.Length
                         && !string.IsNullOrWhiteSpace(args[i + 1]))
                    resumeContextPath = args[++i];
                else if (args[i] == "--show-guidance-from-step" && i + 1 < args.Length
                         && int.TryParse(args[++i], out var parsedShowStep) && parsedShowStep > 0)
                    showGuidanceFromStep = parsedShowStep;
                else if (args[i] == "--hide-guidance")
                    hideGuidance = true;
                else
                    return Usage(texts);
            }

            if (hideGuidance && showGuidanceFromStep is not null)
                return Usage(texts);

            return new(DapLaunchMode.LearnerWeb, args[1], null, startStep, resumeContextPath, showGuidanceFromStep, hideGuidance);
        }

        if (args.Length >= 2 && args[0] == "--learner-windows" && !string.IsNullOrWhiteSpace(args[1]))
        {
            string? windowAutomationId = null;
            int? startStep = null;
            string? resumeContextPath = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--window-automation-id" && i + 1 < args.Length)
                    windowAutomationId = args[++i];
                else if (args[i] == "--start-step" && i + 1 < args.Length
                         && int.TryParse(args[++i], out var parsedStartStep) && parsedStartStep > 0)
                    startStep = parsedStartStep;
                else if (args[i] == "--resume-context-file" && i + 1 < args.Length
                         && !string.IsNullOrWhiteSpace(args[i + 1]))
                    resumeContextPath = args[++i];
                else
                    return Usage(texts);
            }

            if (!string.IsNullOrWhiteSpace(windowAutomationId))
                return new(DapLaunchMode.LearnerWindows, args[1], windowAutomationId, startStep, resumeContextPath, null, false);

            return Usage(texts);
        }

        return Usage(texts);
    }

    private static DapLaunchOptions? Usage(IUiTextProvider texts)
    {
        MessageBox.Show(
            texts.Get("App.Usage"),
            texts.Get("App.WindowTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }
}
