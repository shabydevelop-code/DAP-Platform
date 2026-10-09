using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Browser;

namespace DAP.Runtime.Web.Learner;

public sealed class AdapterWebGuideRuntime
{
    private readonly WebGuideStepRuntime _steps;
    private readonly IWebBrowserAdapter _browser;

    public AdapterWebGuideRuntime(WebGuideStepRuntime steps, IWebBrowserAdapter browser)
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
                Func<CancellationToken, Task>? onTargetObserved = null;
                if (GuideRunPlan.ShouldCapture(step, TargetRuntime.Web, StepCaptureTiming.DuringStep))
                    onTargetObserved = async observationToken =>
                    {
                        var value = await _browser.CaptureAsync(step, observationToken);
                        GuideRunPlan.RecordCapture(plan.Captures, step.Id, value);
                    };
                await _steps.RunActiveStepAsync(
                    step, step.Order, total, token, onTargetObserved: onTargetObserved);
                if (GuideRunPlan.ShouldCapture(step, TargetRuntime.Web, StepCaptureTiming.AfterAction))
                {
                    var value = await _browser.CaptureAsync(step, token);
                    if (value is null)
                        throw new InvalidOperationException($"Guide Step '{step.Id}' declares a Web after-action capture that could not be resolved.");
                    GuideRunPlan.RecordCapture(plan.Captures, step.Id, value);
                }
            },
            (step, total, token) => _browser.WaitForCenteredStepDismissalAsync(step, step.Order, total, token),
            async (step, plan, token) =>
            {
                var value = await _browser.CaptureAsync(step, token);
                if (value is null)
                    throw new InvalidOperationException($"Guide Step '{step.Id}' declares a Web capture that could not be resolved.");
                GuideRunPlan.RecordCapture(plan.Captures, step.Id, value);
            }),
            cancellationToken,
            startStepOrder,
            initialCapturedValues);
    }

}
