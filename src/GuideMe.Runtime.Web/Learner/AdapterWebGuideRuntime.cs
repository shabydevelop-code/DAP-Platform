using GuideMe.Core.Guides;
using GuideMe.Core.Targets;
using GuideMe.Runtime.Web.Browser;

namespace GuideMe.Runtime.Web.Learner;

/// <summary>Web step adapter for the single GuideMe variable execution model.</summary>
public sealed class AdapterWebGuideRuntime
{
    private readonly WebGuideStepRuntime _steps;
    private readonly IWebBrowserAdapter _browser;

    public AdapterWebGuideRuntime(WebGuideStepRuntime steps, IWebBrowserAdapter browser)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    public Task RunAsync(IReadOnlyList<GuideStep> guideSteps, CancellationToken cancellationToken,
        IReadOnlyList<GuideVariableDefinition>? variables = null)
        => new GuideExecutionEngine().RunAsync(guideSteps, CreateStepAdapter(), cancellationToken, variables);

    public IGuideStepAdapter CreateStepAdapter()
        => new DelegateGuideStepAdapter(TargetRuntime.Web, "Web",
            async (step, index, total, plan, token) =>
            {
                Func<CancellationToken, Task>? onTargetObserved = null;
                if (step.VariableActions?.Any(a => a.Timing == StepCaptureTiming.DuringStep) == true)
                    onTargetObserved = observationToken =>
                        plan.Variables.ExecuteActionsAsync(step.VariableActions!,
                            StepCaptureTiming.DuringStep,
                            (action, readToken) => _browser.ReadVariableAsync(step, action, readToken),
                            observationToken);
                await _steps.RunActiveStepAsync(
                    step, step.Order, total, token, onTargetObserved: onTargetObserved);
            },
            (step, total, token) =>
                _browser.WaitForCenteredStepDismissalAsync(step, step.Order, total, token),
            (step, action, token) => _browser.ReadVariableAsync(step, action, token));
}
