using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Windows.Bubbles;
using DAP.Runtime.Windows.Targets;
using DAP.Runtime.Windows.Validation;

namespace DAP.Runtime.Windows.Learner;

public sealed class WindowsGuideRuntime
{
    private readonly WindowsTargetResolver _resolver;
    private readonly WindowsBubblePresenter _bubbles;
    private readonly WindowsValidationEvaluator _validation;
    private readonly TimeSpan _pollInterval;

    public WindowsGuideRuntime(
        WindowsTargetResolver resolver,
        WindowsBubblePresenter bubbles,
        WindowsValidationEvaluator? validation = null,
        TimeSpan? pollInterval = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _validation = validation ?? new WindowsValidationEvaluator();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task RunAsync(
        AutomationElement windowRoot,
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken,
        int? startStepOrder = null)
    {
        var ordered = guideSteps.OrderBy(step => step.Order).ToArray();
        var startIndex = 0;

        if (startStepOrder is not null)
        {
            startIndex = Array.FindIndex(ordered, step => step.Order == startStepOrder.Value);
            if (startIndex < 0)
                throw new InvalidOperationException($"Guide does not contain Step order {startStepOrder.Value}.");
        }

        for (var index = startIndex; index < ordered.Length; index++)
        {
            var step = ordered[index];
            if (step.Target?.Runtime != TargetRuntime.Windows)
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' is not a Windows Step and cannot run in WindowsGuideRuntime.");

            Console.Error.WriteLine($"[DAP Windows guide] starting Step {index + 1}/{ordered.Length} '{step.Id}'.");
            await RunStepAsync(windowRoot, step, index + 1, ordered.Length, cancellationToken);
            Console.Error.WriteLine($"[DAP Windows guide] completed Step {index + 1}/{ordered.Length} '{step.Id}'.");
        }

        await _bubbles.HideAsync();
    }

    private async Task RunStepAsync(
        AutomationElement windowRoot,
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken)
    {
        var clicked = string.Equals(step.Validation?.Kind, "clicked", StringComparison.OrdinalIgnoreCase);
        var targetDisappeared = string.Equals(step.Validation?.Kind, "target-disappeared", StringComparison.OrdinalIgnoreCase);
        var targetWasResolved = false;
        var clickCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationEventHandler? clickHandler = null;
        AutomationElement? subscribedTarget = null;
        string? lastTargetDisappearedDiagnostic = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TargetResolution<AutomationElement> resolution;
                try
                {
                    resolution = _resolver.Resolve(windowRoot, step.Target!);
                }
                catch (ElementNotAvailableException)
                {
                    await _bubbles.HideAsync();
                    if (targetDisappeared && targetWasResolved)
                        return;
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
                }

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    await _bubbles.HideAsync();
                    if (targetDisappeared && targetWasResolved)
                        return;
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
                }

                var target = resolution.Target;
                targetWasResolved = true;

                if (clicked && (subscribedTarget is null || !Automation.Compare(subscribedTarget, target)))
                {
                    if (subscribedTarget is not null && clickHandler is not null)
                    {
                        try { Automation.RemoveAutomationEventHandler(InvokePattern.InvokedEvent, subscribedTarget, clickHandler); }
                        catch (ElementNotAvailableException) { }
                    }

                    clickHandler = (_, _) => clickCompleted.TrySetResult();
                    Automation.AddAutomationEventHandler(
                        InvokePattern.InvokedEvent,
                        target,
                        TreeScope.Element,
                        clickHandler);
                    subscribedTarget = target;
                }

                await _bubbles.ShowAsync(target, step, stepNumber, totalSteps, cancellationToken);

                if (clicked)
                {
                    if (clickCompleted.Task.IsCompleted)
                        return;
                }
                else if (targetDisappeared)
                {
                    // Completion is detected on a later reconciliation pass when
                    // the previously resolved target leaves the current UI tree.
                    var diagnostic = DescribeTarget(target);
                    if (!string.Equals(diagnostic, lastTargetDisappearedDiagnostic, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine(
                            $"[DAP Windows guide] Step '{step.Id}' target-disappeared still resolves: {diagnostic}");
                        lastTargetDisappearedDiagnostic = diagnostic;
                    }
                }
                else if (step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
                         && step.Validation is not null
                         && _validation.IsSatisfied(target, step.Validation))
                {
                    return;
                }
                else if (step.AdvanceMode == StepAdvanceMode.Manual)
                {
                    throw new NotSupportedException("Manual Windows Steps are not implemented yet.");
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }
        }
        finally
        {
            if (subscribedTarget is not null && clickHandler is not null)
            {
                try { Automation.RemoveAutomationEventHandler(InvokePattern.InvokedEvent, subscribedTarget, clickHandler); }
                catch (ElementNotAvailableException) { }
            }
            await _bubbles.HideAsync();
        }
    }

    private static string DescribeTarget(AutomationElement target)
    {
        try
        {
            var parts = new List<string>
            {
                $"AutomationId='{target.Current.AutomationId}'",
                $"Name='{target.Current.Name}'",
                $"ControlType='{target.Current.ControlType?.ProgrammaticName ?? "<null>"}'",
                $"IsOffscreen={target.Current.IsOffscreen}",
                $"IsEnabled={target.Current.IsEnabled}"
            };

            var ancestors = new List<string>();
            var walker = TreeWalker.ControlViewWalker;
            for (var current = walker.GetParent(target);
                 current is not null && ancestors.Count < 6;
                 current = walker.GetParent(current))
            {
                ancestors.Add(
                    $"{current.Current.ControlType?.ProgrammaticName ?? "<null>"}" +
                    $"(AutomationId='{current.Current.AutomationId}',Name='{current.Current.Name}',IsOffscreen={current.Current.IsOffscreen})");
            }

            parts.Add($"Ancestors=[{string.Join(" <- ", ancestors)}]");
            return string.Join("; ", parts);
        }
        catch (ElementNotAvailableException)
        {
            return "<target became unavailable while collecting diagnostics>";
        }
    }
}
