using GuideMe.Core.Guides;
using GuideMe.Core.Targets;
using GuideMe.Runtime.Web.Browser;

namespace GuideMe.Runtime.Web.Learner;

public sealed class AdapterWebGuideRuntime
{
    private readonly WebGuideStepRuntime _steps;
    private readonly IWebBrowserAdapter _browser;
    private readonly Action<WebGuidePresentationPhase>? _onPhase;

    public AdapterWebGuideRuntime(WebGuideStepRuntime steps, IWebBrowserAdapter browser,
        Action<WebGuidePresentationPhase>? onPhase = null)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _onPhase = onPhase;
    }

    public Task RunAsync(
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken)
    {
        return new GuideExecutionEngine().RunAsync(guideSteps, CreateStepAdapter(), cancellationToken);
    }

    public IGuideStepAdapter CreateStepAdapter()
    {
        return new DelegateGuideStepAdapter(TargetRuntime.Web, "Web",
            async (step, index, total, plan, token) =>
            {
                _onPhase?.Invoke(WebGuidePresentationPhase.Transitioning);
                try
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
                }
                finally { _onPhase?.Invoke(WebGuidePresentationPhase.Transitioning); }
            },
            async (step, total, token) =>
            {
                _onPhase?.Invoke(WebGuidePresentationPhase.BubbleVisible);
                try { await _browser.WaitForCenteredStepDismissalAsync(step, step.Order, total, token); }
                finally { _onPhase?.Invoke(WebGuidePresentationPhase.Transitioning); }
            },
            async (step, plan, token) =>
            {
                var value = await _browser.CaptureAsync(step, token);
                if (value is null)
                    throw new InvalidOperationException($"Guide Step '{step.Id}' declares a Web capture that could not be resolved.");
                GuideRunPlan.RecordCapture(plan.Captures, step.Id, value);
            });
    }

}
