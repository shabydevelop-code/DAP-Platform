using DAP.Core.Guides;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebGuideRuntime
{
    private readonly WebLearnerRuntime _steps;

    public WebGuideRuntime(WebLearnerRuntime steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public async Task RunAsync(
        IPage page,
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken)
    {
        foreach (var step in guideSteps.OrderBy(step => step.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step.Target?.Runtime != Core.Targets.TargetRuntime.Web)
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' is not a Web Step and cannot run in WebGuideRuntime.");

            await _steps.RunActiveStepAsync(page, step, cancellationToken);
        }
    }
}
