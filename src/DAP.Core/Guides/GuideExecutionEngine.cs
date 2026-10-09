using DAP.Core.Targets;

namespace DAP.Core.Guides;

/// <summary>
/// Environment-independent execution coordinator. Platform adapters own only
/// platform-specific step operations; ordering, captures and lifecycle live here.
/// </summary>
public sealed class GuideExecutionEngine
{
    public async Task RunAsync(
        IReadOnlyList<GuideStep> steps,
        TargetRuntime runtime,
        Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> executePlatformStep,
        CancellationToken cancellationToken,
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null,
        string? diagnosticName = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(executePlatformStep);

        var plan = new GuideRunPlan(steps, startStepOrder, initialCapturedValues);
        var name = diagnosticName ?? runtime.ToString();

        await plan.ExecuteAsync(
            runtime,
            (step, index, total, token) => executePlatformStep(step, index, total, plan, token),
            cancellationToken,
            onSkipped: step => Console.Error.WriteLine($"[DAP {name} guide] skipped disabled Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onStarting: step => Console.Error.WriteLine($"[DAP {name} guide] starting Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onCompleted: step => Console.Error.WriteLine($"[DAP {name} guide] completed Step {step.Order}/{plan.Steps.Count} '{step.Id}'."));

        Console.Error.WriteLine($"[DAP {name} guide] Guide finished.");
    }
}
