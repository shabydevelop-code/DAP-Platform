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
    Task CaptureAsync(GuideStep step, GuideRunPlan plan, CancellationToken cancellationToken);
}

/// <summary>Typed adapter wrapper for platform-specific step delegates.</summary>
public sealed class DelegateGuideStepAdapter : IGuideStepAdapter
{
    private readonly Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> _execute;
    private readonly Func<GuideStep, int, CancellationToken, Task> _showCentered;
    private readonly Func<GuideStep, GuideRunPlan, CancellationToken, Task> _capture;

    public DelegateGuideStepAdapter(
        TargetRuntime runtime,
        string diagnosticName,
        Func<GuideStep, int, int, GuideRunPlan, CancellationToken, Task> execute,
        Func<GuideStep, int, CancellationToken, Task> showCentered,
        Func<GuideStep, GuideRunPlan, CancellationToken, Task> capture)
    {
        Runtime = runtime;
        DiagnosticName = diagnosticName ?? throw new ArgumentNullException(nameof(diagnosticName));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _showCentered = showCentered ?? throw new ArgumentNullException(nameof(showCentered));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public TargetRuntime Runtime { get; }
    public string DiagnosticName { get; }

    public Task ExecuteAsync(GuideStep step, int index, int total,
        GuideRunPlan plan, CancellationToken cancellationToken)
        => _execute(step, index, total, plan, cancellationToken);

    public Task ShowCenteredInformationAsync(GuideStep step, int total, CancellationToken cancellationToken)
        => _showCentered(step, total, cancellationToken);

    public Task CaptureAsync(GuideStep step, GuideRunPlan plan, CancellationToken cancellationToken)
        => _capture(step, plan, cancellationToken);
}

/// <summary>
/// Shared cancellation-aware reconciliation scheduler for active target steps.
/// Adapters own observations and rendering; the core owns iteration, cancellation
/// and the decision to stop once a step has completed.
/// </summary>
public sealed class GuideStepReconciliationEngine
{
    public async IAsyncEnumerable<bool> TicksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return true;
            await Task.Yield();
        }
    }

    public async Task RunAsync(
        Func<CancellationToken, Task<bool>> reconcile,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reconcile);
        if (interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await reconcile(cancellationToken))
                return;
            await Task.Delay(interval, cancellationToken);
        }
    }
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
                {
                    if (step.Capture is not null)
                        await adapter.CaptureAsync(step, plan, token);
                    await adapter.ExecuteAsync(step, index, total, plan, token);
                }
            },
            cancellationToken,
            onSkipped: step => Console.Error.WriteLine($"[DAP {diagnosticName} guide] skipped disabled Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onStarting: step => Console.Error.WriteLine($"[DAP {diagnosticName} guide] starting Step {step.Order}/{plan.Steps.Count} '{step.Id}'."),
            onCompleted: step => Console.Error.WriteLine($"[DAP {diagnosticName} guide] completed Step {step.Order}/{plan.Steps.Count} '{step.Id}'."));
        Console.Error.WriteLine($"[DAP {diagnosticName} guide] Guide finished.");
    }
}
