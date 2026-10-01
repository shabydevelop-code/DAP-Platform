using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Bubbles;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebLearnerRuntime
{
    private readonly WebBubblePresenter _bubbles;
    private readonly WebStepContextGuard _contextGuard;
    private readonly TimeSpan _reconcileInterval;

    public WebLearnerRuntime(
        WebBubblePresenter bubbles,
        WebStepContextGuard? contextGuard = null,
        TimeSpan? reconcileInterval = null)
    {
        _bubbles = bubbles;
        _contextGuard = contextGuard ?? new WebStepContextGuard();
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

                if (resolution.Status != TargetResolutionStatus.Resolved)
                    await _bubbles.HideAsync(page);
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
