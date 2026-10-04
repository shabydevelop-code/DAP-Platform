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
        if (step.Bubble.Placement == BubblePlacement.Center)
        {
            if (step.Target is not null)
                throw new InvalidOperationException(
                    $"Centered Guide Step '{step.Id}' must not define a target.");
            if (step.AdvanceMode != StepAdvanceMode.Manual || step.Validation is not null)
                throw new InvalidOperationException(
                    $"Centered Guide Step '{step.Id}' must use Manual advance with no validation.");

            await _bubbles.WaitForCenteredStepDismissalAsync(
                page,
                step,
                stepNumber,
                totalSteps,
                cancellationToken);
            return;
        }

        if (step.Target is null)
            throw new InvalidOperationException(
                $"Target-attached Guide Step '{step.Id}' must define a target.");

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
                        // Once the click event is observed, the source target has
                        // already fulfilled the primary validation. Do not resolve
                        // that retiring/source target again while navigation or a
                        // rerender is in flight. Persisted completion conditions
                        // alone decide when the destination/business transition
                        // is complete.
                        if (await AreCompletionConditionsSatisfiedAsync(page, step, cancellationToken))
                            return;

                        _validationSession.Trace(
                            $"[DAP runtime] persisted completion conditions pending for clicked Step '{step.Id}'.");
                        await Task.Delay(_reconcileInterval, cancellationToken);
                        continue;
                    }

                    var completedResolution = await _bubbles.ResolveTargetAsync(page, step, cancellationToken);
                    if (completedResolution.Status == TargetResolutionStatus.Resolved
                        && completedResolution.Target is not null)
                    {
                        var primaryValidationSatisfied = await _validation.IsSatisfiedAsync(
                            completedResolution.Target,
                            step.Validation!,
                            cancellationToken);

                        if (primaryValidationSatisfied
                            && await AreCompletionConditionsSatisfiedAsync(page, step, cancellationToken))
                        {
                            await _bubbles.HideAsync(page);
                            return;
                        }

                        if (!primaryValidationSatisfied)
                        {
                            // Non-click commit events represent one learner commit
                            // attempt. If the committed value is invalid, consume
                            // that attempt so subsequent typing cannot advance until
                            // a new blur/change commit event is observed.
                            _validationSession.ConsumeCompletion(step.Id);
                        }
                    }
                }

                if (!await _contextGuard.IsActiveAsync(page, step, cancellationToken))
                {
                    _validationSession.Trace($"[DAP runtime trace] Step '{step.Id}' context inactive; hiding bubble.");
                    await _bubbles.HideAsync(page);
                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }

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

                        if (await AreCompletionConditionsSatisfiedAsync(page, step, cancellationToken))
                            return;

                        await Task.Delay(_reconcileInterval, cancellationToken);
                        continue;
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
                if (await AreCompletionConditionsSatisfiedAsync(page, step, cancellationToken))
                    return;
            }

            await Task.Delay(_reconcileInterval, cancellationToken);
        }
    }

    private static async Task<bool> AreCompletionConditionsSatisfiedAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken)
    {
        if (step.CompletionConditions is null || step.CompletionConditions.Count == 0)
            return true;

        foreach (var condition in step.CompletionConditions)
        {
            if (condition.Target.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException(
                    $"Web Guide Step '{step.Id}' contains a non-Web completion target.");

            var target = await ResolveCompletionTargetAsync(page, condition.Target, cancellationToken);
            var kind = condition.Kind.Trim().ToLowerInvariant();

            if (kind == "target-exists")
            {
                if (target is null)
                    return false;
                continue;
            }

            if (kind == "target-not-exists")
            {
                if (target is not null)
                    return false;
                continue;
            }

            if (kind == "target-enabled")
            {
                if (target is null || !await target.IsEnabledAsync())
                    return false;
                continue;
            }

            if (kind == "value-equals")
            {
                if (target is null || condition.ExpectedValue is null)
                    return false;
                if (!string.Equals(
                        await target.InputValueAsync(),
                        condition.ExpectedValue,
                        StringComparison.Ordinal))
                    return false;
                continue;
            }

            throw new NotSupportedException(
                $"Unsupported Web completion condition kind '{condition.Kind}'.");
        }

        return true;
    }

    private static async Task<ILocator?> ResolveCompletionTargetAsync(
        IPage page,
        TargetDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        IFrame frame = page.MainFrame;
        if (descriptor.FrameContext is not null)
        {
            foreach (var frameLocator in descriptor.FrameContext.Path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frameElement = frameLocator.Strategy.Trim().ToLowerInvariant() switch
                {
                    "css" => frame.Locator(frameLocator.Value),
                    "text" => frame.GetByText(frameLocator.Value),
                    "label" => frame.GetByLabel(frameLocator.Value),
                    "role" when Enum.TryParse<AriaRole>(frameLocator.Value, true, out var frameRole) => frame.GetByRole(frameRole),
                    _ => throw new NotSupportedException(
                        $"Unsupported Web completion frame locator strategy '{frameLocator.Strategy}'.")
                };

                if (await frameElement.CountAsync() != 1)
                    return null;

                var handle = await frameElement.ElementHandleAsync();
                var child = handle is null ? null : await handle.ContentFrameAsync();
                if (child is null || child.IsDetached)
                    return null;
                frame = child;
            }
        }

        var locator = descriptor.Locator.Strategy.Trim().ToLowerInvariant() switch
        {
            "css" => frame.Locator(descriptor.Locator.Value),
            "text" => frame.GetByText(descriptor.Locator.Value),
            "label" => frame.GetByLabel(descriptor.Locator.Value),
            "role" when Enum.TryParse<AriaRole>(descriptor.Locator.Value, true, out var role) => frame.GetByRole(role),
            _ => throw new NotSupportedException(
                $"Unsupported Web completion locator strategy '{descriptor.Locator.Strategy}'.")
        };

        return await locator.CountAsync() == 1 ? locator : null;
    }

    public Task WaitForGuideCompletedDismissalAsync(IPage page, CancellationToken cancellationToken)
        => _bubbles.WaitForGuideCompletedDismissalAsync(page, cancellationToken);

    public async Task StopAsync(IPage page)
    {
        await _bubbles.HideAsync(page);
        await _validationSession.DisposeAsync();
    }
}
