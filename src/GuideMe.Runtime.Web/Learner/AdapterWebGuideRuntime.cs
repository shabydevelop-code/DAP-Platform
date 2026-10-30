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
        IReadOnlyList<GuideVariableDefinition>? variables = null, string language = "he")
        => new GuideExecutionEngine().RunAsync(guideSteps, CreateStepAdapter(), cancellationToken, variables, language);

    public IGuideStepAdapter CreateStepAdapter()
        => new DelegateGuideStepAdapter(TargetRuntime.Web, "Web",
            (step, index, total, plan, token) =>
                _steps.RunActiveStepAsync(step, step.Order, total, token),
            (step, total, token) =>
                _browser.WaitForCenteredStepDismissalAsync(step, step.Order, total, token),
            (step, action, token) => _browser.ReadVariableAsync(step, action, token));
}
