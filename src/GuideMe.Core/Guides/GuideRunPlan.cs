using GuideMe.Core.Targets;

namespace GuideMe.Core.Guides;

/// <summary>One ordered guide execution and one session-scoped variable store.</summary>
public sealed class GuideRunPlan
{
    private readonly GuideStep[] _steps;
    public IReadOnlyList<GuideStep> Steps => _steps;
    public GuideVariableSession Variables { get; }

    public GuideRunPlan(IReadOnlyList<GuideStep> steps,
        IReadOnlyList<GuideVariableDefinition>? variables = null)
    {
        _steps = steps.OrderBy(step => step.Order).ToArray();
        Variables = new GuideVariableSession(variables ?? Array.Empty<GuideVariableDefinition>());
    }

    public int NextEnabledIndex(int currentIndex) =>
        Array.FindIndex(_steps, currentIndex + 1, step => step.IsEnabled);

    public GuideStep Materialize(GuideStep step) => Variables.Materialize(step);

    public GuideStep? TryMaterialize(GuideStep step)
    {
        try { return Materialize(step); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Execute the same ordered step and variable lifecycle for one runtime.</summary>
    public async Task ExecuteAsync(
        TargetRuntime runtime,
        Func<GuideStep, int, int, CancellationToken, Task> executeStep,
        CancellationToken cancellationToken,
        Action<GuideStep>? onSkipped = null,
        Action<GuideStep>? onStarting = null,
        Action<GuideStep>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(executeStep);
        for (var index = 0; index < _steps.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = _steps[index];
            if (!persisted.IsEnabled) { onSkipped?.Invoke(persisted); continue; }
            var step = Materialize(persisted);
            if (step.Target is not null && step.Target.Runtime != runtime)
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' is not a {runtime} Step.");
            onStarting?.Invoke(step);
            await executeStep(step, index, _steps.Length, cancellationToken);
            onCompleted?.Invoke(step);
        }
    }
}
