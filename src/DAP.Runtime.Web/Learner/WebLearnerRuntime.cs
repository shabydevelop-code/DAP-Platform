using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Bubbles;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebLearnerRuntime
{
    private readonly WebBubblePresenter _bubbles;
    private readonly TimeSpan _reconcileInterval;

    public WebLearnerRuntime(
        WebBubblePresenter bubbles,
        TimeSpan? reconcileInterval = null)
    {
        _bubbles = bubbles;
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
