using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Bubbles;
using DAP.Runtime.Web.Validation;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebLearnerRuntime
{
    private readonly WebBubblePresenter _bubbles;
    private readonly WebStepContextGuard _contextGuard;
    private readonly WebValidationEvaluator _validation;
    private readonly TimeSpan _reconcileInterval;

    public WebLearnerRuntime(
        WebBubblePresenter bubbles,
        WebStepContextGuard? contextGuard = null,
        WebValidationEvaluator? validation = null,
        TimeSpan? reconcileInterval = null)
    {
        _bubbles = bubbles;
        _contextGuard = contextGuard ?? new WebStepContextGuard();
        _validation = validation ?? new WebValidationEvaluator();
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task RunActiveStepAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await _contextGuard.IsActiveAsync(page, step, cancellationToken))
                {
                    await _bubbles.HideAsync(page);
                    await Task.Delay(_reconcileInterval, cancellationToken);
                    continue;
                }

                var resolution = await _bubbles.EnsureShownAsync(page, step, cancellationToken);

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    await _bubbles.HideAsync(page);
                }
                else if (step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
                    && step.Validation is not null
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

    public Task StopAsync(IPage page) => _bubbles.HideAsync(page);
}
