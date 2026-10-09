using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Browser;

namespace DAP.Runtime.Web.Learner;

public sealed class AdapterWebGuideRuntime
{
    private static readonly Regex RuntimeValueToken = new(
        @"\{\{step:(?<step>[^}:]+):capture\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        var captured = plan.Captures;
        var ordered = plan.Steps;

        for (var i=plan.StartIndex;i<ordered.Length;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persistedStep = ordered[i];
            if (!persistedStep.IsEnabled)
            {
                Console.Error.WriteLine($"[DAP guide] skipped disabled Step {persistedStep.Order}/{ordered.Length} '{persistedStep.Id}'.");
                continue;
            }

            var step = plan.Materialize(persistedStep);
            if (step.Target is not null && step.Target.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException($"Guide Step '{step.Id}' is not a Web Step.");

            if (step.Capture is not null)
            {
                var value = await _browser.CaptureAsync(step, cancellationToken);
                if (value is null) throw new InvalidOperationException($"Guide Step '{step.Id}' declares a Web capture that could not be resolved.");
                captured[step.Id]=value;
            }

            await _steps.RunActiveStepAsync(
                step, step.Order, ordered.Length, cancellationToken,
                true,
                () => Console.Error.WriteLine($"[DAP guide] starting Step {step.Order}/{ordered.Length} '{step.Id}'."));
            Console.Error.WriteLine($"[DAP guide] completed Step {step.Order}/{ordered.Length} '{step.Id}'.");
        }

        Console.Error.WriteLine("[DAP guide] Guide finished.");
    }

}
