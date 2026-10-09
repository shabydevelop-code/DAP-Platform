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
    Task ShowCenteredInformationAsync(GuideStep step, int total, CancellationToken cancellationToken);
}

/// <summary>Typed adapter wrapper for platform-specific step delegates.</summary>
public sealed class DelegateGuideStepAdapter : IGuideStepAdapter
{
    private readonly Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> _execute;
    private readonly Func<GuideStep, int, CancellationToken, Task> _showCentered;

    public DelegateGuideStepAdapter(
        TargetRuntime runtime,
        string diagnosticName,
        Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> execute,
        Func<GuideStep, int, CancellationToken, Task> showCentered)
    {
        Runtime = runtime;
        DiagnosticName = diagnosticName ?? throw new ArgumentNullException(nameof(diagnosticName));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _showCentered = showCentered ?? throw new ArgumentNullException(nameof(showCentered));
    }

    public TargetRuntime Runtime { get; }
    public string DiagnosticName { get; }

    public Task ExecuteAsync(GuideStep step, int index, int total,
        GuideRunPlan plan, CancellationToken cancellationToken)
        => _execute(step, index, total, plan, cancellationToken);

    public Task ShowCenteredInformationAsync(GuideStep step, int total, CancellationToken cancellationToken)
        => _showCentered(step, total, cancellationToken);
}

public sealed class GuideExecutionEngine
{
    public async Task RunAsync(
        IReadOnlyList<GuideStep> steps,
        IGuideStepAdapter adapter,
        CancellationToken cancellationToken,
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(adapter);
        var runtime = adapter.Runtime;
        var diagnosticName = adapter.DiagnosticName;

        var plan = new GuideRunPlan(steps, startStepOrder, initialCapturedValues);
        await plan.ExecuteAsync(
            runtime,
            async (step, index, total, token) =>
            {
                var presentation = GuideStepExecutionPolicy.Classify(step);
                if (presentation == GuideStepPresentationKind.CenteredInformation)
                    await adapter.ShowCenteredInformationAsync(step, total, token);
                else
                    await adapter.ExecuteAsync(step, index, total, plan, token);
            },
            cancellationToken,
            onSkipped: step => Console.Error.WriteLine($"[DAP {diagnosticName} guide] skipped disabled Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onStarting: step => Console.Error.WriteLine($"[DAP {diagnosticName} guide] starting Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onCompleted: step => Console.Error.WriteLine($"[DAP {diagnosticName} guide] completed Step {step.Order}/{plan.Steps.Count} '{step.Id}'."));
        Console.Error.WriteLine($"[DAP {diagnosticName} guide] Guide finished.");
    }
}
