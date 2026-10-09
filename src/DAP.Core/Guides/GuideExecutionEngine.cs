using DAP.Core.Targets;

namespace DAP.Core.Guides;

/// <summary>
/// Environment-independent execution coordinator. Platform adapters own only
/// platform-specific step operations; ordering, captures and lifecycle live here.
/// </summary>
/// <summary>Platform boundary for an active guide step. The engine owns ordering,
/// materialization, validation of step shape and lifecycle diagnostics.</summary>
public interface IGuideStepAdapter
{
    TargetRuntime Runtime { get; }
    string DiagnosticName { get; }
    Task ExecuteAsync(GuideStep step, int index, int total, GuideRunPlan plan, CancellationToken cancellationToken);
}

/// <summary>Typed adapter wrapper for platform-specific step delegates.</summary>
public sealed class DelegateGuideStepAdapter : IGuideStepAdapter
{
    private readonly Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> _execute;

    public DelegateGuideStepAdapter(
        TargetRuntime runtime,
        string diagnosticName,
        Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> execute)
    {
        Runtime = runtime;
        DiagnosticName = diagnosticName ?? throw new ArgumentNullException(nameof(diagnosticName));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public TargetRuntime Runtime { get; }
    public string DiagnosticName { get; }

    public Task ExecuteAsync(GuideStep step, int index, int total,
        GuideRunPlan plan, CancellationToken cancellationToken)
        => _execute(step, index, total, plan, cancellationToken);
}

public sealed class GuideExecutionEngine
{
    public Task RunAsync(
        IReadOnlyList<GuideStep> steps,
        IGuideStepAdapter adapter,
        CancellationToken cancellationToken,
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return RunAsync(steps, adapter.Runtime, adapter.ExecuteAsync,
            cancellationToken, startStepOrder, initialCapturedValues, adapter.DiagnosticName);
    }

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
            async (step, index, total, token) =>
            {
                // All enabled, materialized steps are checked by the shared engine
                // before any platform-specific target resolution or presentation.
                GuideStepExecutionPolicy.Classify(step);
                await executePlatformStep(step, index, total, plan, token);
            },
            cancellationToken,
            onSkipped: step => Console.Error.WriteLine($"[DAP {name} guide] skipped disabled Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onStarting: step => Console.Error.WriteLine($"[DAP {name} guide] starting Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onCompleted: step => Console.Error.WriteLine($"[DAP {name} guide] completed Step {step.Order}/{plan.Steps.Count} '{step.Id}'."));

        Console.Error.WriteLine($"[DAP {name} guide] Guide finished.");
    }
}
