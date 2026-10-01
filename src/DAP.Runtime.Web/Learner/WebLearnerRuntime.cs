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
        var first = await _bubbles.ResolveTargetAsync(page, step, cancellationToken);
        if (first.Status != TargetResolutionStatus.Resolved || first.Target is null)
            return false;

        var firstState = await first.Target.EvaluateAsync<TargetVisualState>(
            "(el) => { const r = el.getBoundingClientRect(); return { connected: el.isConnected, x: r.x, y: r.y, width: r.width, height: r.height }; }");
        if (!firstState.Connected || firstState.Width <= 0 || firstState.Height <= 0)
            return false;

        await Task.Delay(_presentationSettleInterval, cancellationToken);

        var second = await _bubbles.ResolveTargetAsync(page, step, cancellationToken);
        if (second.Status != TargetResolutionStatus.Resolved || second.Target is null)
            return false;

        // The target must still be the same DOM node after the settling window.
        // A selector matching a replacement node while a server render is in
        // progress is intentionally treated as unstable.
        var sameNode = await second.Target.EvaluateAsync<bool>(
            "(el, previous) => el === previous",
            await first.Target.ElementHandleAsync());
        if (!sameNode)
            return false;

        var secondState = await second.Target.EvaluateAsync<TargetVisualState>(
            "(el) => { const r = el.getBoundingClientRect(); return { connected: el.isConnected, x: r.x, y: r.y, width: r.width, height: r.height }; }");
        if (!secondState.Connected)
            return false;

        const double tolerance = 0.5;
        return Math.Abs(firstState.X - secondState.X) <= tolerance
            && Math.Abs(firstState.Y - secondState.Y) <= tolerance
            && Math.Abs(firstState.Width - secondState.Width) <= tolerance
            && Math.Abs(firstState.Height - secondState.Height) <= tolerance;
    }

    private sealed record TargetVisualState(bool Connected, double X, double Y, double Width, double Height);

    public async Task RunActiveStepAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken)
    {
        var hasAutomaticValidation = step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
            && step.Validation is not null;
        var isClickedValidation = hasAutomaticValidation
            && string.Equals(step.Validation!.Kind, "clicked", StringComparison.Ordinal);

        if (hasAutomaticValidation)
            await _validationSession.EnsureBridgeAsync(page);

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
                        await _bubbles.HideAsync(page);
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

                if (!await _contextGuard.IsActiveAsync(page, step, cancellationToken))
                {
                    await _bubbles.HideAsync(page);
                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }

                // A newly active Step is not presented merely because its selector
                // already exists. Server-backed UIs can expose that selector while
                // replacing/reflowing the document. Require the same DOM node and
                // geometry to survive a short settling window first.
                if (!await IsStableForPresentationAsync(page, step, cancellationToken))
                {
                    await _bubbles.HideAsync(page);
                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }

                var presentation = Stopwatch.StartNew();
                var resolution = await _bubbles.EnsureShownAsync(page, step, cancellationToken);

                if (!_firstBubbleReported
                    && resolution.Status == TargetResolutionStatus.Resolved
                    && resolution.Target is not null)
                {
                    _firstBubbleReported = true;
                    Console.Error.WriteLine($"[DAP runtime] first bubble presentation completed ({presentation.Elapsed.TotalMilliseconds:F0} ms active-step work).");
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

            await Task.Delay(_reconcileInterval, cancellationToken);
        }
    }

    public async Task StopAsync(IPage page)
    {
        await _bubbles.HideAsync(page);
        await _validationSession.DisposeAsync();
    }
}
