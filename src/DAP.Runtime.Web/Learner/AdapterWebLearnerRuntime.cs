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
    private readonly TimeSpan _stableReconcileInterval;
    private readonly TimeSpan _presentationSettleInterval;
    private readonly string? _automaticStepLabel;
    private readonly bool _hybrid;

    public AdapterWebLearnerRuntime(
        IWebBrowserAdapter browser,
        TimeSpan? reconcileInterval = null,
        TimeSpan? presentationSettleInterval = null,
        string? automaticStepLabel = null,
        bool hybrid = false)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
        _stableReconcileInterval = TimeSpan.FromMilliseconds(500);
        _presentationSettleInterval = presentationSettleInterval ?? TimeSpan.FromMilliseconds(250);
        _automaticStepLabel = automaticStepLabel;
        _hybrid = hybrid;
    }

    public async Task RunActiveStepAsync(
        GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken,
        bool showPresentation = true, Action? onReady = null)
    {
        if (GuideStepExecutionPolicy.Classify(step) == GuideStepPresentationKind.CenteredInformation)
        {

            if (showPresentation)
                await _browser.WaitForCenteredStepDismissalAsync(step, stepNumber, totalSteps, cancellationToken);
            else
                onReady?.Invoke();
            return;
        }

        var automatic = GuideStepExecutionPolicy.IsAutomaticValidationStep(step);
        var clicked = GuideStepExecutionPolicy.IsClickValidationStep(step);
        // Every target-attached Step must wait for a quiet DOM window before
        // exposing its target. The previous Step may have completed as soon as
        // its persisted completion condition became true while the application
        // is still finishing the same asynchronous render.
        var presentationGatePassed = false;
        var readySignaled = false;
        var hybridValueApplied = false;
        Task<WebValidationCommit?>? commitTask = automatic
            ? _browser.WaitForValidationCommitAsync(step, cancellationToken)
            : null;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (commitTask?.IsCompletedSuccessfully == true)
            {
                var primary = clicked || await _browser.IsPrimaryValidationSatisfiedAsync(step, cancellationToken);
                if (primary)
                {
                    if (GuideStepExecutionPolicy.CanAdvance(
                        primary, await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken)))
                    {
                        await _browser.HideBubbleAsync(cancellationToken);
                        return;
                    }

                    // Preserve the proven runtime behavior exactly:
                    // once a non-click natural commit satisfies the primary
                    // validation, keep that completion latched while a
                    // server-driven completion condition is still pending.
                    // The learner must not be forced to change the control a
                    // second time merely because the DOM/document refresh
                    // completed after the original change event.
                }
                else if (!clicked)
                {
                    // Only an invalid non-click commit is consumed. A valid
                    // commit remains completed until its persisted completion
                    // conditions become true.
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

            WebBubblePresentation presentation;
            if (clicked && commitTask is not null)
            {
                var presentationTask = _browser.EnsureBubbleShownAsync(
                    step, stepNumber, totalSteps, showPresentation, cancellationToken,
                    !string.IsNullOrEmpty(step.AutomationValue) ? _automaticStepLabel : null);
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

                    if (await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                    {
                        await _browser.HideBubbleAsync(cancellationToken);
                        return;
                    }

                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }

                presentation = await presentationTask;
            }
            else
            {
                presentation = await _browser.EnsureBubbleShownAsync(
                    step, stepNumber, totalSteps, showPresentation, cancellationToken,
                    !string.IsNullOrEmpty(step.AutomationValue) ? _automaticStepLabel : null);
            }

            if (presentation.Status != WebTargetResolutionStatus.Resolved)
            {
                await _browser.HideBubbleAsync(cancellationToken);
                presentationGatePassed = false;
            }
            else if (!readySignaled)
            {
                // EnsureBubbleShownAsync returns only after the production
                // adapter has resolved the target and bound the current
                // validation arm. This is the Runtime's natural readiness
                // boundary for learner input, independent of presentation.
                readySignaled = true;
                onReady?.Invoke();
            }

            if (_hybrid && !hybridValueApplied && presentation.Status == WebTargetResolutionStatus.Resolved
                && !string.IsNullOrEmpty(step.AutomationValue))
            {
                GuideStepExecutionPolicy.RequireHybridValueStep(step, "Web");
                await _browser.ApplyAutomationValueAsync(step, cancellationToken);
                hybridValueApplied = true;
            }

            // Re-check click completion after reconciliation. The browser event
            // can arrive just after the presentation race was decided.
            if (clicked && commitTask?.IsCompletedSuccessfully == true)
            {
                if (await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
                {
                    await _browser.HideBubbleAsync(cancellationToken);
                    return;
                }
            }

            // Once the target and bubble are already resolved, keep the exact
            // same reconciliation semantics but avoid hammering the browser
            // every 100 ms while the learner is idle. Transient recovery paths
            // above still use the original 100 ms interval.
            if (commitTask is not null)
            {
                var delay = Task.Delay(_stableReconcileInterval, cancellationToken);
                await Task.WhenAny(commitTask, delay);
            }
            else
                await Task.Delay(_stableReconcileInterval, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _browser.HideBubbleAsync(cancellationToken);
}
