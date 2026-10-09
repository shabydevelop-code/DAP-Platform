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
        bool showPresentation = true, Action? onReady = null,
        Func<CancellationToken, Task>? onTargetObserved = null)
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
        var activeState = new GuideActiveStepState(step);
        var sharedEngine = new UnifiedGuideStepEngine();
        Task<WebValidationCommit?>? commitTask = automatic
            ? _browser.WaitForValidationCommitAsync(step, cancellationToken)
            : null;

        async Task<GuideStepReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
        {
            if (commitTask?.IsCompletedSuccessfully == true)
            {
                var primary = clicked || await _browser.IsPrimaryValidationSatisfiedAsync(step, cancellationToken);
                if (primary)
                {
                    if (sharedEngine.EvaluateCompletion(activeState, 
                        primary,
                        await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken)) == GuideStepReconciliationResult.Completed)
                    {
                        await _browser.HideBubbleAsync(cancellationToken);
                        return GuideStepReconciliationResult.Completed;
                    }

                    // Preserve the proven runtime behavior exactly:
                    // once a non-click natural commit satisfies the primary
                    // validation, keep that completion latched while a
                    // server-driven completion condition is still pending.
                    // The learner must not be forced to change the control a
                    // second time merely because the DOM/document refresh
                    // completed after the original change event.
                }
                else if (GuideStepExecutionPolicy.ShouldConsumeInvalidCommit(step, commitObserved: true, primarySatisfied: primary))
                {
                    // Only an invalid non-click commit is consumed. A valid
                    // commit remains completed until its persisted completion
                    // conditions become true.
                    await _browser.ConsumeValidationCommitAsync(step, cancellationToken);
                    commitTask = _browser.WaitForValidationCommitAsync(step, cancellationToken);
                }
            }

            if (activeState.ObserveContext(await _browser.IsContextActiveAsync(step, cancellationToken))
                == GuideStepReconciliationResult.WaitingForContext)
            {
                await _browser.HideBubbleAsync(cancellationToken);
                return await GuideActiveStepState.WaitAsync(
                    GuideStepReconciliationResult.WaitingForContext, _reconcileInterval, cancellationToken);
            }

            if (!activeState.PresentationReady)
            {
                if (activeState.ObservePresentationStability(
                    await _browser.IsStableForPresentationAsync(step, _presentationSettleInterval, cancellationToken))
                    == GuideStepReconciliationResult.WaitingForTarget)
                {
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForTarget, _reconcileInterval, cancellationToken);
                }
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

                    if (sharedEngine.EvaluateCompletion(activeState, 
                        true,
                        await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken)) == GuideStepReconciliationResult.Completed)
                    {
                        await _browser.HideBubbleAsync(cancellationToken);
                        return GuideStepReconciliationResult.Completed;
                    }

                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForValidation, _reconcileInterval, cancellationToken);
                }

                presentation = await presentationTask;
            }
            else
            {
                presentation = await _browser.EnsureBubbleShownAsync(
                    step, stepNumber, totalSteps, showPresentation, cancellationToken,
                    !string.IsNullOrEmpty(step.AutomationValue) ? _automaticStepLabel : null);
            }

            if (sharedEngine.ObserveReadiness(
                activeState, contextActive: true,
                targetAvailable: presentation.Status == WebTargetResolutionStatus.Resolved, targetVisible: true)
                == GuideStepReconciliationResult.WaitingForTarget)
            {
                await _browser.HideBubbleAsync(cancellationToken);
            }
            else if (activeState.TrySignalReady())
            {
                // EnsureBubbleShownAsync returns only after the production
                // adapter has resolved the target and bound the current
                // validation arm. This is the Runtime's natural readiness
                // boundary for learner input, independent of presentation.
                onReady?.Invoke();
            }

            // Observe captures only against a resolved target. The guide layer
            // owns persistence and the shared core owns capture timing.
            if (presentation.Status == WebTargetResolutionStatus.Resolved && onTargetObserved is not null)
                await onTargetObserved(cancellationToken);

            if (activeState.ShouldApplyHybridValue(_hybrid, presentation.Status == WebTargetResolutionStatus.Resolved))
            {
                Console.Error.WriteLine($"[DAP Web hybrid] applying persisted value for Step {stepNumber}/{totalSteps} '{step.Id}'.");
                try
                {
                    GuideStepExecutionPolicy.RequireHybridValueStep(step, "Web");
                    await _browser.ApplyAutomationValueAsync(step, cancellationToken);
                    activeState.MarkHybridValueApplied();
                    Console.Error.WriteLine($"[DAP Web hybrid] applied persisted value for Step {stepNumber}/{totalSteps} '{step.Id}'.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine($"[DAP Web hybrid] failed Step {stepNumber}/{totalSteps} '{step.Id}': {ex}");
                    throw;
                }
            }

            // Re-check click completion after reconciliation. The browser event
            // can arrive just after the presentation race was decided.
            if (clicked && commitTask?.IsCompletedSuccessfully == true)
            {
                if (sharedEngine.EvaluateCompletion(activeState, 
                    true,
                    await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken)) == GuideStepReconciliationResult.Completed)
                {
                    await _browser.HideBubbleAsync(cancellationToken);
                    return GuideStepReconciliationResult.Completed;
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
                return GuideStepReconciliationResult.WaitingForValidation;
            }
            return await GuideActiveStepState.WaitAsync(
                GuideStepReconciliationResult.WaitingForAction, _stableReconcileInterval, cancellationToken);
        }

        await sharedEngine.RunAsync(activeState, (_, token) => ReconcileAsync(token), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _browser.HideBubbleAsync(cancellationToken);
}
