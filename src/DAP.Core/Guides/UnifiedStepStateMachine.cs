namespace DAP.Core.Guides;

/// <summary>Observations supplied by a platform adapter, never inferred by the engine.</summary>
public sealed record UnifiedStepObservation(
    bool ContextActive,
    bool TargetAvailable,
    bool TargetVisible,
    bool PresentationStable,
    bool PrimarySatisfied,
    bool CompletionConditionsSatisfied,
    bool IsTextEditTarget = false,
    bool TextEditCommitted = false);

/// <summary>Declarative decision returned to Web or Windows without platform-specific effects.</summary>
public sealed record UnifiedStepDecision(
    GuideStepReconciliationResult Status,
    bool ShowPresentation,
    bool HidePresentation,
    bool StepCompleted);

/// <summary>
/// A new platform-neutral state machine. Adapters observe the environment and execute
/// presentation effects; the engine alone classifies context, readiness and completion.
/// </summary>
public sealed class UnifiedStepStateMachine
{
    private readonly GuideActiveStepState _state;

    public UnifiedStepStateMachine(GuideStep step)
    {
        _state = new GuideActiveStepState(step);
    }

    public UnifiedStepDecision Advance(UnifiedStepObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!observation.ContextActive)
        {
            _state.ObserveContext(false);
            return new(GuideStepReconciliationResult.WaitingForContext, false, true, false);
        }

        if (!observation.TargetAvailable || !observation.TargetVisible)
        {
            _state.ObservePresentationAvailability(observation.TargetAvailable, observation.TargetVisible);
            return new(GuideStepReconciliationResult.WaitingForTarget, false, true, false);
        }

        if (!observation.PresentationStable)
        {
            _state.ObservePresentationStability(false);
            return new(GuideStepReconciliationResult.WaitingForTarget, false, true, false);
        }

        _state.ObservePresentationStability(true);
        var completion = _state.EvaluateCompletion(
            observation.PrimarySatisfied,
            observation.CompletionConditionsSatisfied,
            observation.IsTextEditTarget,
            observation.TextEditCommitted);

        if (completion == GuideStepReconciliationResult.Completed)
            return new(completion, false, true, true);

        return new(completion, true, false, false);
    }
}
