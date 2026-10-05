using DAP.Core.Guides;
using DAP.Runtime.Web.Browser;

namespace DAP.Runtime.Web.Learner;

/// <summary>
/// Browser-independent active-step policy. This is the migration target for the
/// existing WebLearnerRuntime policy; browser mechanics are delegated only.
/// </summary>
public sealed class AdapterWebLearnerRuntime
{
    private readonly IWebBrowserAdapter _browser;
    private readonly TimeSpan _reconcileInterval;
    private readonly TimeSpan _presentationSettleInterval;
    private bool _firstBubbleReported;

    public AdapterWebLearnerRuntime(
        IWebBrowserAdapter browser,
        TimeSpan? reconcileInterval = null,
        TimeSpan? presentationSettleInterval = null)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
        _presentationSettleInterval = presentationSettleInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task RunActiveStepAsync(
        GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken)
    {
        if (step.Bubble.Placement == BubblePlacement.Center)
        {
            if (step.Target is not null
                || step.AdvanceMode != StepAdvanceMode.Manual
                || step.Validation is not null
                || step.Context is not null
                || step.Capture is not null
                || step.CompletionConditions is { Count: > 0 })
                throw new InvalidOperationException($"Centered Guide Step '{step.Id}' must be a pure Manual information Step with no target, context, validation, capture, or completion conditions.");

            await _browser.WaitForCenteredStepDismissalAsync(step, stepNumber, totalSteps, cancellationToken);
            _firstBubbleReported = true;
            return;
        }

        if (step.Target is null)
            throw new InvalidOperationException($"Target-attached Guide Step '{step.Id}' must define a target.");

        var automatic = step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation && step.Validation is not null;
        var clicked = automatic && string.Equals(step.Validation!.Kind, "clicked", StringComparison.Ordinal);
        var presentationGatePassed = !_firstBubbleReported;
        Task<WebValidationCommit?>? commitTask = automatic
            ? _browser.WaitForValidationCommitAsync(step, cancellationToken)
            : null;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (commitTask?.IsCompletedSuccessfully == true)
            {
                var primary = clicked || await _browser.IsPrimaryValidationSatisfiedAsync(step, cancellationToken);
                if (primary && await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                {
                    await _browser.HideBubbleAsync(cancellationToken);
                    return;
                }

                // Invalid non-click commit: wait for the learner's next natural
                // commit event. Do not poll intermediate input values.
                if (!clicked)
                {
                    // The event is a one-shot natural commit (blur/change).
                    // Remove it before arming the next wait; otherwise the
                    // already-completed session would immediately satisfy the
                    // new wait and turn value validation into polling.
                    await _browser.ConsumeValidationCommitAsync(step, cancellationToken);
                    commitTask = _browser.WaitForValidationCommitAsync(step, cancellationToken);
                }
            }

            if (!await _browser.IsContextActiveAsync(step, cancellationToken))
            {
                await _browser.HideBubbleAsync(cancellationToken);
                await Task.Delay(_reconcileInterval, cancellationToken);
                continue;
            }

            if (!presentationGatePassed)
            {
                if (!await _browser.IsStableForPresentationAsync(step, _presentationSettleInterval, cancellationToken))
                {
                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }
                presentationGatePassed = true;
            }

            var presentation = await _browser.EnsureBubbleShownAsync(step, stepNumber, totalSteps, cancellationToken);
            if (presentation.Status == WebTargetResolutionStatus.Resolved)
                _firstBubbleReported = true;
            else
            {
                await _browser.HideBubbleAsync(cancellationToken);
                presentationGatePassed = false;
            }

            if (commitTask is not null)
            {
                var delay = Task.Delay(_reconcileInterval, cancellationToken);
                await Task.WhenAny(commitTask, delay);
            }
            else
                await Task.Delay(_reconcileInterval, cancellationToken);
        }
    }

    public Task WaitForGuideCompletedDismissalAsync(CancellationToken cancellationToken = default)
        => _browser.WaitForGuideCompletedDismissalAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _browser.HideBubbleAsync(cancellationToken);
}
