using System.Text.RegularExpressions;
using DAP.Core.Targets;

namespace DAP.Core.Guides;

/// <summary>Runtime-neutral guide sequencing and captured-value substitution.</summary>
public sealed class GuideRunPlan
{
    private static readonly Regex CaptureToken = new(
        @"\{\{step:(?<step>[^}:]+):capture\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly GuideStep[] _steps;
    public IReadOnlyList<GuideStep> Steps => _steps;
    public int StartIndex { get; }
    public Dictionary<string, string> Captures { get; }

    public GuideRunPlan(
        IReadOnlyList<GuideStep> steps,
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null)
    {
        _steps = steps.OrderBy(step => step.Order).ToArray();
        StartIndex = startStepOrder is null
            ? 0
            : Array.FindIndex(_steps, step => step.Order == startStepOrder.Value);
        if (StartIndex < 0)
            throw new InvalidOperationException($"Guide does not contain Step order {startStepOrder}.");

        Captures = initialCapturedValues is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(initialCapturedValues, StringComparer.Ordinal);
    }

    /// <summary>Explicit timing is shared across runtimes. For older guides,
    /// retain the established Web/Windows observation schedule.</summary>
    public static bool ShouldCapture(GuideStep step, TargetRuntime runtime, StepCaptureTiming timing)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Capture is null)
            return false;
        if (step.Capture.Timing is { } configured)
            return configured == timing;
        return runtime switch
        {
            TargetRuntime.Web => timing == StepCaptureTiming.BeforeAction,
            TargetRuntime.Windows => timing is StepCaptureTiming.DuringStep or StepCaptureTiming.AfterAction,
            _ => throw new ArgumentOutOfRangeException(nameof(runtime))
        };
    }

    /// <summary>Store an observed capture only when it is available and changed.
    /// Platform adapters retain control of when the value is observed.</summary>
    public static bool RecordCapture(IDictionary<string, string> captures, string stepId, string? value)
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        if (value is null)
            return false;
        if (captures.TryGetValue(stepId, out var existing)
            && string.Equals(existing, value, StringComparison.Ordinal))
            return false;
        captures[stepId] = value;
        return true;
    }

    /// <summary>Execute shared step lifecycle; environment-specific work stays in the callback.</summary>
    public async Task ExecuteAsync(
        TargetRuntime runtime,
        Func<GuideStep, int, int, CancellationToken, Task> executeStep,
        CancellationToken cancellationToken,
        Action<GuideStep>? onSkipped = null,
        Action<GuideStep>? onStarting = null,
        Action<GuideStep>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(executeStep);
        for (var index = StartIndex; index < _steps.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = _steps[index];
            if (!persisted.IsEnabled)
            {
                onSkipped?.Invoke(persisted);
                continue;
            }

            var step = Materialize(persisted);
            if (step.Target is not null && step.Target.Runtime != runtime)
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' is not a {runtime} Step.");

            onStarting?.Invoke(step);
            await executeStep(step, index, _steps.Length, cancellationToken);
            onCompleted?.Invoke(step);
        }
    }

    public int NextEnabledIndex(int currentIndex) =>
        Array.FindIndex(_steps, currentIndex + 1, step => step.IsEnabled);

    public GuideStep Materialize(GuideStep step)
    {
        if (step.Target is null)
            return step;

        string Replace(string value) => CaptureToken.Replace(value, match =>
        {
            var source = match.Groups["step"].Value;
            if (!Captures.TryGetValue(source, out var captured))
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' references uncaptured runtime value from Step '{source}'.");
            return captured;
        });

        Locator MaterializeLocator(Locator locator) =>
            locator with { Value = Replace(locator.Value) };

        return step with
        {
            Target = step.Target with
            {
                Locator = MaterializeLocator(step.Target.Locator),
                Anchors = step.Target.Anchors.Select(anchor =>
                    anchor with { Locator = MaterializeLocator(anchor.Locator) }).ToArray()
            }
        };
    }

    public GuideStep? TryMaterialize(GuideStep step)
    {
        try { return Materialize(step); }
        catch (InvalidOperationException) { return null; }
    }
}
