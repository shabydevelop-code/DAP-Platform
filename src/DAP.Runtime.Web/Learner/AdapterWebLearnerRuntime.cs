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
    private readonly TimeSpan _stableSafetyInterval;
    private bool _firstBubbleReported;

    public AdapterWebLearnerRuntime(
        IWebBrowserAdapter browser,
        TimeSpan? reconcileInterval = null,
        TimeSpan? presentationSettleInterval = null)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
        _presentationSettleInterval = presentationSettleInterval ?? TimeSpan.FromMilliseconds(250);
        _stableSafetyInterval = TimeSpan.FromSeconds(1);
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
        var validCommitLatched = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!validCommitLatched && commitTask?.IsCompletedSuccessfully == true)
            {
                var primary = clicked || await _browser.IsPrimaryValidationSatisfiedAsync(step, cancellationToken);
                if (primary)
                {
                    validCommitLatched = true;
                    if (await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                    {
                        await _browser.HideBubbleAsync(cancellationToken);
                        return;
                    }

                    // A valid commit stays latched while persisted completion
                    // conditions are pending. Do not re-enter a completed Task
                    // on every loop iteration; wait for browser invalidation.
                }
                else if (!clicked)
                {
                    await _browser.ConsumeValidationCommitAsync(step, cancellationToken);
                    commitTask = _browser.WaitForValidationCommitAsync(step, cancellationToken);
                }
            }
            else if (validCommitLatched)
            {
                if (await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                {
                    await _browser.HideBubbleAsync(cancellationToken);
                    return;
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

            WebBubblePresentation presentation;
            if (clicked && commitTask is not null)
            {
                var presentationTask = _browser.EnsureBubbleShownAsync(step, stepNumber, totalSteps, cancellationToken);
                var winner = await Task.WhenAny(presentationTask, commitTask);

                if (winner == commitTask && commitTask.IsCompletedSuccessfully)
                {
                    // A validating click may replace the source document while
                    // presentation is still reconciling against it. Completion
                    // must win that race; never wait on the retiring document.
                    _ = presentationTask.ContinueWith(
                        completed => _ = completed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);

                    validCommitLatched = true;
                    if (await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                    {
                        await _browser.HideBubbleAsync(cancellationToken);
                        return;
                    }

                    var invalidatedAfterClick = _browser.WaitForPresentationInvalidationAsync(cancellationToken);
                    var clickSafety = Task.Delay(_stableSafetyInterval, cancellationToken);
                    await Task.WhenAny(invalidatedAfterClick, clickSafety);
                    continue;
                }

                presentation = await presentationTask;
            }
            else
            {
                presentation = await _browser.EnsureBubbleShownAsync(step, stepNumber, totalSteps, cancellationToken);
            }

            if (presentation.Status == WebTargetResolutionStatus.Resolved)
                _firstBubbleReported = true;
            else
            {
                await _browser.HideBubbleAsync(cancellationToken);
                presentationGatePassed = false;
            }

            // Re-check click completion after reconciliation. The browser event
            // can arrive just after the presentation race was decided.
            if (!validCommitLatched && clicked && commitTask?.IsCompletedSuccessfully == true)
            {
                validCommitLatched = true;
                if (await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                {
                    await _browser.HideBubbleAsync(cancellationToken);
                    return;
                }
            }

            // Stable presentation is event-driven. Mutation events relevant to
            // the active target wake the Runtime immediately. A low-frequency
            // safety wake keeps context/completion semantics robust without the
            // former 100 ms browser polling loop.
            var invalidationTask = _browser.WaitForPresentationInvalidationAsync(cancellationToken);
            var safetyTask = Task.Delay(_stableSafetyInterval, cancellationToken);

            if (commitTask is not null && !validCommitLatched)
                await Task.WhenAny(commitTask, invalidationTask, safetyTask);
            else
                await Task.WhenAny(invalidationTask, safetyTask);
        }
    }

    public Task WaitForGuideCompletedDismissalAsync(CancellationToken cancellationToken = default)
        => _browser.WaitForGuideCompletedDismissalAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _browser.HideBubbleAsync(cancellationToken);
}
