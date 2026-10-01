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
    private bool _firstBubbleReported;

    public WebLearnerRuntime(
        WebBubblePresenter bubbles,
        WebStepContextGuard? contextGuard = null,
        WebValidationEvaluator? validation = null,
        WebValidationSession? validationSession = null,
        TimeSpan? reconcileInterval = null)
    {
        _bubbles = bubbles;
        _contextGuard = contextGuard ?? new WebStepContextGuard();
        _validation = validation ?? new WebValidationEvaluator();
        _validationSession = validationSession ?? new WebValidationSession();
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task RunActiveStepAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken)
    {
        var isClickedValidation = step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
            && string.Equals(step.Validation?.Kind, "clicked", StringComparison.Ordinal);

        if (isClickedValidation)
            await _validationSession.EnsureBridgeAsync(page);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Event completion lives in DAP.exe and is checked before context
                // or presentation. The validating click may itself replace the
                // document or navigate away from the Step context.
                if (isClickedValidation && _validationSession.IsCompleted(step.Id))
                {
                    await _bubbles.HideAsync(page);
                    return;
                }

                if (!await _contextGuard.IsActiveAsync(page, step, cancellationToken))
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
                else if (step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
                    && step.Validation is not null
                    && !isClickedValidation
                    && await _validation.IsSatisfiedAsync(resolution.Target, step.Validation, cancellationToken))
                {
                    await _bubbles.HideAsync(page);
                    return;
                }
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
