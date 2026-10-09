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
        var plan = new GuideRunPlan(guideSteps, startStepOrder, initialCapturedValues);
        await plan.ExecuteAsync(
            TargetRuntime.Web,
            async (step, index, total, token) =>
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
            cancellationToken,
            onSkipped: step => Console.Error.WriteLine($"[DAP guide] skipped disabled Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onStarting: step => Console.Error.WriteLine($"[DAP guide] starting Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onCompleted: step => Console.Error.WriteLine($"[DAP guide] completed Step {step.Order}/{plan.Steps.Count} '{step.Id}'."));

        Console.Error.WriteLine("[DAP guide] Guide finished.");
    }

}
