using DAP.Core.Guides;
using DAP.Runtime.Web.Browser;

namespace DAP.Runtime.Web.Learner;

/// <summary>
/// Browser-independent active-step policy. Browser mechanics are delegated to the Web adapter; shared step policy lives in Core.
/// </summary>
public sealed class WebGuideStepRuntime
{
    private static readonly object ReferenceDiagnosticGate = new();
    private static readonly string ReferenceDiagnosticPath = Path.Combine(Path.GetTempPath(), "GuideMe-reference-diagnostic.log");
    private static void ReferenceDiagnostic(string message)
    {
        try
        {
            lock (ReferenceDiagnosticGate)
                File.AppendAllText(ReferenceDiagnosticPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private readonly IWebBrowserAdapter _browser;
    private readonly TimeSpan _reconcileInterval;
    private readonly TimeSpan _stableReconcileInterval;
    private readonly TimeSpan _presentationSettleInterval;

    public WebGuideStepRuntime(
        IWebBrowserAdapter browser,
        TimeSpan? reconcileInterval = null,
        TimeSpan? presentationSettleInterval = null)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
        _stableReconcileInterval = TimeSpan.FromMilliseconds(500);
        _presentationSettleInterval = presentationSettleInterval ?? TimeSpan.FromMilliseconds(250);
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

        var diagnoseReference = step.Id.StartsWith("reference-", StringComparison.Ordinal);
        if (diagnoseReference) ReferenceDiagnostic($"[DAP reference diagnostic] Starting Web Step {stepNumber}/{totalSteps} '{step.Id}'.");
        var automatic = GuideStepExecutionPolicy.IsAutomaticValidationStep(step);
        var clicked = GuideStepExecutionPolicy.IsClickValidationStep(step);
        // Opt-in observation-only steps do not arm browser input listeners.
        // Existing Guides retain their original validation semantics.
        var stateKind = step.Validation?.Kind;
        var stateValue = step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
            && (string.Equals(stateKind, "state-value-equals", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stateKind, "state-value-not-empty", StringComparison.OrdinalIgnoreCase));
        var observationOnly = step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
            && string.Equals(stateKind, "state-observed", StringComparison.OrdinalIgnoreCase)
            && step.CompletionConditions is { Count: > 0 };
        var initialObservationRecorded = false;
        var observationSawUnsatisfied = false;
        // Every target-attached Step must wait for a quiet DOM window before
        // exposing its target. The previous Step may have completed as soon as
        // its persisted completion condition became true while the application
        // is still finishing the same asynchronous render.
        var activeState = new GuideActiveStepState(step);
        var sharedEngine = new UnifiedGuideStepEngine();
        Task<WebValidationCommit?>? commitTask = automatic && !observationOnly && !stateValue
            ? _browser.WaitForValidationCommitAsync(step, cancellationToken)
            : null;

        async Task<GuideStepReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
        {
            if (stateValue && activeState.PresentationReady
                && await _browser.IsPrimaryValidationSatisfiedAsync(step, cancellationToken)
                && await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken))
            {
                await _browser.HideBubbleAsync(cancellationToken);
                return GuideStepReconciliationResult.Completed;
            }
            if (observationOnly && activeState.PresentationReady)
            {
                var satisfied = await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken);
                if (!initialObservationRecorded)
                {
                    initialObservationRecorded = true;
                    if (satisfied)
                        Console.Error.WriteLine($"[DAP state observation] Step {stepNumber}/{totalSteps} '{step.Id}': completion condition already true; waiting for it to become false and then true.");
                }
                if (!satisfied)
                    observationSawUnsatisfied = true;
                // A reused DOM marker can be true on entry. Require a false->true
                // observation within this step; do not permanently latch its initial state.
                if (observationSawUnsatisfied && satisfied)
                {
                    await _browser.HideBubbleAsync(cancellationToken);
                    return GuideStepReconciliationResult.Completed;
                }
            }

            if (diagnoseReference && step.Id == "reference-web-save")
            {
                var clickObserved = commitTask?.IsCompletedSuccessfully == true;
                var destinationObserved = await _browser.AreCompletionConditionsSatisfiedAsync(step, cancellationToken);
                ReferenceDiagnostic($"[DAP reference diagnostic] Save gate: clickObserved={clickObserved}, destinationObserved={destinationObserved}.");
            }

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

            var contextActive = await _browser.IsContextActiveAsync(step, cancellationToken);
            if (diagnoseReference && !contextActive) ReferenceDiagnostic($"[DAP reference diagnostic] Step '{step.Id}' waiting for active context.");
            if (sharedEngine.ObserveContext(activeState, contextActive)
                == GuideStepReconciliationResult.WaitingForContext)
            {
                await _browser.HideBubbleAsync(cancellationToken);
                return await GuideActiveStepState.WaitAsync(
                    GuideStepReconciliationResult.WaitingForContext, _reconcileInterval, cancellationToken);
            }

            if (!activeState.PresentationReady)
            {
                var presentationStable = await _browser.IsStableForPresentationAsync(step, _presentationSettleInterval, cancellationToken);
                if (diagnoseReference && !presentationStable) ReferenceDiagnostic($"[DAP reference diagnostic] Step '{step.Id}' waiting for stable/interactable target.");
                if (activeState.ObservePresentationStability(
                    presentationStable)
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
                    null);
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
                    null);
            }

            if (diagnoseReference) ReferenceDiagnostic($"[DAP reference diagnostic] Step '{step.Id}' bubble resolution: {presentation.Status}.");
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
