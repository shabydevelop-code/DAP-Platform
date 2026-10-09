using System.Windows;
using DAP.Core.Localization;

namespace DAP.App;

public enum DapLaunchMode
{
    InfrastructureCheck,
    Learner
}

public enum DapExecutionMode
{
    Manual,
    Hybrid
}

public sealed record DapLaunchOptions(
    DapLaunchMode Mode,
    string? GuideId,
    string? WindowAutomationId,
    int? StartStep,
    string? ResumeContextPath,
    DapExecutionMode ExecutionMode = DapExecutionMode.Manual)
{
    public static DapLaunchOptions? Parse(string[] args, IUiTextProvider texts)
    {
        if (args.Length == 1 && args[0] == "--check")
            return new(DapLaunchMode.InfrastructureCheck, null, null, null, null);

        if (args.Length >= 2
            && args[0] == "--guide"
            && !string.IsNullOrWhiteSpace(args[1]))
        {
            string? windowAutomationId = null;
            var executionMode = DapExecutionMode.Manual;
            var modeSpecified = false;
            int? startStep = null;
            string? resumeContextPath = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--mode" && i + 1 < args.Length && !modeSpecified)
                {
                    modeSpecified = true;
                    executionMode = args[++i] switch
                    {
                        "manual" => DapExecutionMode.Manual,
                        "hybrid" => DapExecutionMode.Hybrid,
                        _ => (DapExecutionMode)(-1)
                    };
                    if (!Enum.IsDefined(executionMode)) return Usage(texts);
                }
                else if (args[i] == "--window-automation-id" && i + 1 < args.Length
                    && !string.IsNullOrWhiteSpace(args[i + 1]))
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

            return new(
                DapLaunchMode.Learner,
                args[1],
                windowAutomationId,
                startStep,
                resumeContextPath,
                executionMode);
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
