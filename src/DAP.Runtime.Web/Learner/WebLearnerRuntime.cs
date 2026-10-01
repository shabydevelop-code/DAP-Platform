using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Bubbles;
using DAP.Runtime.Web.Validation;
using Microsoft.Playwright;
using System.Diagnostics;

namespace DAP.Runtime.Web.Learner;

public sealed class WebLearnerRuntime
{
    private readonly WebBubblePresenter _bubbles;
    private readonly WebStepContextGuard _contextGuard;
    private readonly WebValidationEvaluator _validation;
    private readonly WebValidationSession _validationSession;
    private readonly TimeSpan _reconcileInterval;
    private readonly TimeSpan _presentationSettleInterval;
    private bool _firstBubbleReported;

    public WebLearnerRuntime(
        WebBubblePresenter bubbles,
        WebStepContextGuard? contextGuard = null,
        WebValidationEvaluator? validation = null,
        WebValidationSession? validationSession = null,
        TimeSpan? reconcileInterval = null,
        TimeSpan? presentationSettleInterval = null)
    {
        _bubbles = bubbles;
        _contextGuard = contextGuard ?? new WebStepContextGuard();
        _validation = validation ?? new WebValidationEvaluator();
        _validationSession = validationSession ?? new WebValidationSession();
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
        _presentationSettleInterval = presentationSettleInterval ?? TimeSpan.FromMilliseconds(250);
    }

    private async Task<bool> IsStableForPresentationAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken)
    {
        var resolution = await _bubbles.ResolveTargetAsync(page, step, cancellationToken);
        if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
            return false;

        // Locators are live queries, not frozen DOM-node references. Instead of
        // pretending a Locator proves node identity, observe the target's actual
        // document for a quiet window. Any subtree/attribute/text mutation resets
        // the quiet timer. This catches server-driven DOM replacement and local
        // rerendering without requiring a network request to exist.
        return await resolution.Target.EvaluateAsync<bool>(
            @"(el, settleMs) => new Promise(resolve => {
                if (!el.isConnected) { resolve(false); return; }

                const doc = el.ownerDocument;
                let timer;
                const finish = () => {
                    observer.disconnect();
                    const r = el.getBoundingClientRect();
                    resolve(el.isConnected && r.width > 0 && r.height > 0);
                };
                const reset = () => {
                    clearTimeout(timer);
                    timer = setTimeout(finish, settleMs);
                };
                const observer = new MutationObserver(reset);
                observer.observe(doc.documentElement, {
                    subtree: true,
                    childList: true,
                    attributes: true,
                    characterData: true
                });
                reset();
            })",
            _presentationSettleInterval.TotalMilliseconds);
    }

    public async Task RunActiveStepAsync(
        IPage page,
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken)
    {
        var hasAutomaticValidation = step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
            && step.Validation is not null;
        var isClickedValidation = hasAutomaticValidation
            && string.Equals(step.Validation!.Kind, "clicked", StringComparison.Ordinal);

        if (hasAutomaticValidation)
            await _validationSession.EnsureBridgeAsync(page);

        // Settling is a one-time presentation gate for this Step. Once the
        // Step has actually been presented, normal reconciliation owns it and
        // must not repeatedly send the bubble back through transition settling.
        var presentationGatePassed = !_firstBubbleReported;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Event completion lives in DAP.exe and is checked before context
                // or presentation. The validating click may itself replace the
                // document or navigate away from the Step context.
                if (hasAutomaticValidation && _validationSession.IsCompleted(step.Id))
                {
                    // An event says the learner finished interacting; the
                    // validation condition still decides whether it was valid.
                    // Click validation is itself satisfied by the click event.
                    if (isClickedValidation)
                    {
                        _validationSession.Trace($"[DAP runtime] click completion at loop entry for Step '{step.Id}'.");
                        // The browser click handler removes the active bubble
                        // synchronously before reporting completion. Do not run a
                        // second cross-frame cleanup here: the validating click
                        // may already be replacing its frame, and evaluating a
                        // retiring frame can block Guide advancement.
                        return;
                    }

                    var completedResolution = await _bubbles.ResolveTargetAsync(page, step, cancellationToken);
                    if (completedResolution.Status == TargetResolutionStatus.Resolved
                        && completedResolution.Target is not null
                        && await _validation.IsSatisfiedAsync(completedResolution.Target, step.Validation!, cancellationToken))
                    {
                        await _bubbles.HideAsync(page);
                        return;
                    }
                }

                _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' entering context check.");
                if (!await _contextGuard.IsActiveAsync(page, step, cancellationToken))
                {
                    _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' context inactive; hiding bubble.");
                    await _bubbles.HideAsync(page);
                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }
                _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' context check completed active.");

                // The first Step has no preceding learner transition to settle.
                // For later Steps, wait until the target document has been quiet
                // before presenting the next instruction.
                if (!presentationGatePassed)
                {
                    _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' entering presentation stability check.");
                    if (!await IsStableForPresentationAsync(page, step, cancellationToken))
                    {
                        await Task.Delay(_reconcileInterval, cancellationToken);
                        continue;
                    }

                    presentationGatePassed = true;
                    _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' presentation stability check completed.");
                }

                _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' entering EnsureShown.");
                var presentation = Stopwatch.StartNew();

                TargetResolution<ILocator> resolution;
                if (isClickedValidation)
                {
                    var presentationTask = _bubbles.EnsureShownAsync(
                        page,
                        step,
                        stepNumber,
                        totalSteps,
                        cancellationToken);
                    var completionTask = _validationSession.WaitForCompletionAsync(step.Id);

                    var winner = await Task.WhenAny(presentationTask, completionTask);
                    if (winner == completionTask && _validationSession.IsCompleted(step.Id))
                    {
                        _validationSession.Trace(
                            $"[DAP runtime] click completion won race with in-flight EnsureShown for Step '{step.Id}'.");

                        // Do not await a Playwright operation that is already talking
                        // to the document being replaced by the validating click.
                        // Observe any eventual fault so the abandoned task cannot
                        // become an unobserved exception.
                        _ = presentationTask.ContinueWith(
                            completed => _ = completed.Exception,
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        return;
                    }

                    resolution = await presentationTask;
                }
                else
                {
                    resolution = await _bubbles.EnsureShownAsync(
                        page,
                        step,
                        stepNumber,
                        totalSteps,
                        cancellationToken);
                }

                _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' EnsureShown completed with {resolution.Status}.");

                if (resolution.Status == TargetResolutionStatus.Resolved
                    && resolution.Target is not null)
                {
                    if (!_firstBubbleReported)
                    {
                        _firstBubbleReported = true;
                        Console.Error.WriteLine($"[DAP runtime] first bubble presentation completed ({presentation.Elapsed.TotalMilliseconds:F0} ms active-step work).");
                    }
                }
                else if (presentationGatePassed)
                {
                    // The gate only counts as passed once a presentation can
                    // actually resolve. If the target disappeared in the small
                    // gap between settling and presentation, require settling
                    // again before its first visible presentation.
                    presentationGatePassed = false;
                }

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    await _bubbles.HideAsync(page);
                }
                // Non-click value validation is intentionally not polled for
                // completion here. Its condition is evaluated only after the
                // control reports its natural commit event (blur/change).
            }
            catch (PlaywrightException) when (!cancellationToken.IsCancellationRequested)
            {
                // Navigation/frame replacement can invalidate the document between
                // resolution and presentation. The next reconciliation resolves it again.
            }

            // A browser callback can report completion while this iteration is
            // already awaiting a Playwright operation against a document that is
            // being replaced. Check the DAP-owned completion state again before
            // sleeping/reconciling so click Steps can advance independently of
            // the retiring document.
            if (isClickedValidation && _validationSession.IsCompleted(step.Id))
            {
                _validationSession.Trace($"[DAP runtime] click completion after reconciliation for Step '{step.Id}'.");
                return;
            }

            await Task.Delay(_reconcileInterval, cancellationToken);
        }
    }

    public Task WaitForGuideCompletedDismissalAsync(IPage page, CancellationToken cancellationToken)
        => _bubbles.WaitForGuideCompletedDismissalAsync(page, cancellationToken);

    public async Task StopAsync(IPage page)
    {
        await _bubbles.HideAsync(page);
        await _validationSession.DisposeAsync();
    }
}
