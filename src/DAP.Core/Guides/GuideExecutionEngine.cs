using DAP.Core.Targets;

namespace DAP.Core.Guides;

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

/// <summary>Shared per-step lifecycle state. An adapter records platform observations;
/// the core decides whether the step may finish or needs another reconciliation.</summary>
public sealed class GuideActiveStepState
{
    private readonly GuideStep _step;
    public GuideActiveStepState(GuideStep step)
    {
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    public bool HybridValueApplied { get; private set; }
    public bool PresentationReady { get; private set; }
    public bool ReadySignaled { get; private set; }

    public void MarkPresentationReady() => PresentationReady = true;
    public void InvalidatePresentation() => PresentationReady = false;
    public bool TrySignalReady()
    {
        if (ReadySignaled) return false;
        ReadySignaled = true;
        return true;
    }
    public bool ShouldApplyHybridValue(bool hybrid, bool targetResolved)
        => hybrid && targetResolved && !HybridValueApplied && !string.IsNullOrEmpty(_step.AutomationValue);
    public void MarkHybridValueApplied()
    {
        if (HybridValueApplied)
            throw new InvalidOperationException($"Hybrid automation for Step '{_step.Id}' was already applied.");
        HybridValueApplied = true;
    }

    /// <summary>Delay and return the next lifecycle state without changing platform polling cadence.</summary>
    public static async Task<GuideStepReconciliationResult> WaitAsync(
        GuideStepReconciliationResult reason, TimeSpan interval, CancellationToken cancellationToken)
    {
        if (reason == GuideStepReconciliationResult.Completed)
            throw new ArgumentException("A completed step cannot wait.", nameof(reason));
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        await Task.Delay(interval, cancellationToken);
        return reason;
    }
    /// <summary>Shared reaction to a missing context or target. Rendering stays
    /// platform-specific, while presentation invalidation is a core decision.</summary>
    public GuideStepReconciliationResult ObserveContext(bool contextActive)
    {
        if (contextActive)
            return GuideStepReconciliationResult.WaitingForAction;
        InvalidatePresentation();
        return GuideStepReconciliationResult.WaitingForContext;
    }

    public GuideStepReconciliationResult ObserveTarget(bool targetResolved)
    {
        if (targetResolved)
            return GuideStepReconciliationResult.WaitingForAction;
        InvalidatePresentation();
        return GuideStepReconciliationResult.WaitingForTarget;
    }

    /// <summary>Classify a target that cannot currently be presented. A missing
    /// target must not be mistaken for an observed learner action.</summary>
    public GuideStepReconciliationResult ObservePresentationAvailability(bool targetAvailable, bool visible)
    {
        if (!targetAvailable)
            return ObserveTarget(false);
        if (!visible)
        {
            InvalidatePresentation();
            return GuideStepReconciliationResult.WaitingForTarget;
        }
        return GuideStepReconciliationResult.WaitingForAction;
    }

    public GuideStepReconciliationResult ObservePresentationStability(bool stable)
    {
        if (!stable)
        {
            InvalidatePresentation();
            return GuideStepReconciliationResult.WaitingForTarget;
        }
        MarkPresentationReady();
        return GuideStepReconciliationResult.WaitingForAction;
    }


    public GuideStepReconciliationResult EvaluateCompletion(
        bool primarySatisfied, bool completionConditionsSatisfied,
        bool isTextEditTarget = false, bool textEditCommitted = false)
        => GuideStepExecutionPolicy.IsStepComplete(
            _step, primarySatisfied, completionConditionsSatisfied, isTextEditTarget, textEditCommitted)
            ? GuideStepReconciliationResult.Completed
            : GuideStepReconciliationResult.WaitingForValidation;
}

/// <summary>Platform-neutral result of a single active-step observation.</summary>
public enum GuideStepReconciliationResult
{
    WaitingForContext,
    WaitingForTarget,
    WaitingForAction,
    WaitingForValidation,
    Completed
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
