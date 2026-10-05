using System.Text.RegularExpressions;
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
        var captured = initialCapturedValues is null
            ? new Dictionary<string,string>(StringComparer.Ordinal)
            : new Dictionary<string,string>(initialCapturedValues, StringComparer.Ordinal);
        var ordered = guideSteps.OrderBy(x=>x.Order).ToArray();
        var start = 0;
        if (startStepOrder is not null)
        {
            start = Array.FindIndex(ordered, x=>x.Order==startStepOrder.Value);
            if (start < 0) throw new InvalidOperationException($"Guide does not contain Step order {startStepOrder.Value}.");
        }

        for (var i=start;i<ordered.Length;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = MaterializeRuntimeValues(ordered[i], captured);
            if (step.Target is not null && step.Target.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException($"Guide Step '{step.Id}' is not a Web Step.");

            if (step.Capture is not null)
            {
                var value = await _browser.CaptureAsync(step, cancellationToken);
                if (value is null) throw new InvalidOperationException($"Guide Step '{step.Id}' declares a Web capture that could not be resolved.");
                captured[step.Id]=value;
            }

            Console.Error.WriteLine($"[DAP guide] starting Step {i+1}/{ordered.Length} '{step.Id}'.");
            await _steps.RunActiveStepAsync(step, i+1, ordered.Length, cancellationToken);
            Console.Error.WriteLine($"[DAP guide] completed Step {i+1}/{ordered.Length} '{step.Id}'.");
        }

        Console.Error.WriteLine("[DAP guide] presenting completion bubble.");
        await _steps.WaitForGuideCompletedDismissalAsync(cancellationToken);
        Console.Error.WriteLine("[DAP guide] completion bubble dismissed; Guide finished.");
    }

    private static GuideStep MaterializeRuntimeValues(GuideStep step, IReadOnlyDictionary<string,string> values)
    {
        if (step.Target is null) return step;
        Locator Mat(Locator l) => l with { Value = RuntimeValueToken.Replace(l.Value, m => {
            var id=m.Groups["step"].Value;
            if(!values.TryGetValue(id,out var v)) throw new InvalidOperationException($"Guide Step '{step.Id}' references runtime capture from Step '{id}', but that Step has not captured one.");
            return v.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);
        })};
        return step with { Target = step.Target with { Locator=Mat(step.Target.Locator), Anchors=step.Target.Anchors.Select(a=>a with { Locator=Mat(a.Locator) }).ToArray() } };
    }
}
