using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Browser;

namespace DAP.Runtime.Web.Learner;

public sealed class AdapterWebGuideRuntime
{
    private readonly AdapterWebLearnerRuntime _steps;
    private readonly IWebBrowserAdapter _browser;

    public AdapterWebGuideRuntime(AdapterWebLearnerRuntime steps, IWebBrowserAdapter browser)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    public async Task RunAsync(
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken,
        int? startStepOrder = null,
        IReadOnlyDictionary<string,string>? initialCapturedValues = null)
    {
        await new GuideExecutionEngine().RunAsync(
            guideSteps,
            new DelegateGuideStepAdapter(TargetRuntime.Web, "Web",
            async (step, index, total, plan, token) =>
            {
                if (step.Capture is not null)
                {
                    var value = await _browser.CaptureAsync(step, token);
                    if (value is null)
                        throw new InvalidOperationException($"Guide Step '{step.Id}' declares a Web capture that could not be resolved.");
                    plan.Captures[step.Id] = value;
                }

                await _steps.RunActiveStepAsync(step, step.Order, total, token);
            },
            (step, total, token) => _browser.WaitForCenteredStepDismissalAsync(step, step.Order, total, token)),
            cancellationToken,
            startStepOrder,
            initialCapturedValues);
    }

}
