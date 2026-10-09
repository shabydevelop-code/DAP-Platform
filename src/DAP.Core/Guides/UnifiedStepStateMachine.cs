namespace DAP.Core.Guides;

/// <summary>Facts measured by a platform adapter. No platform objects cross this boundary.</summary>
public sealed record UnifiedStepObservation(
    bool ContextActive,
    bool TargetAvailable,
    bool TargetVisible,
    bool PresentationStable,
    bool PrimarySatisfied,
    bool CompletionConditionsSatisfied,
    bool IsTextEditTarget = false,
    bool TextEditCommitted = false,
    bool ActionObserved = false,
    bool TargetWasPreviouslyAvailable = false);

public enum UnifiedPresentationAction { None, Show, Hide }

/// <summary>A decision to execute; adapters never decide whether a step advances.</summary>
public sealed record UnifiedStepDecision(
    GuideStepReconciliationResult Status,
    bool ShowPresentation,
    bool HidePresentation,
    bool StepCompleted,
    UnifiedPresentationAction PresentationAction = UnifiedPresentationAction.None);

/// <summary>
/// Standalone state machine for one active step. The adapter reports facts and
/// performs the requested visual effects; the core owns transition decisions.
/// </summary>
public sealed class UnifiedStepStateMachine
{
    private readonly GuideActiveStepState _state;
    private readonly GuideStep _step;
    private bool _presentationVisible;
    private bool _completed;
    private bool _actionLatched;
    private bool _targetEverAvailable;

    public UnifiedStepStateMachine(GuideStep step)
    {
        _step = step ?? throw new ArgumentNullException(nameof(step));
        _state = new GuideActiveStepState(step);
    }

    public bool Completed => _completed;

    public UnifiedStepDecision Advance(UnifiedStepObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (_completed)
            return new(GuideStepReconciliationResult.Completed, false, false, true);

        _targetEverAvailable |= observation.TargetAvailable || observation.TargetWasPreviouslyAvailable;
        _actionLatched |= observation.ActionObserved;

        // A click or disappearance can complete after navigation has retired its
        // source context. Check the latched action before target readiness.
        bool primary = observation.PrimarySatisfied;
        if (GuideStepExecutionPolicy.IsClickValidationStep(_step))
            primary |= _actionLatched;
        if (GuideStepExecutionPolicy.IsTargetDisappearanceStep(_step))
            primary |= _targetEverAvailable && !observation.TargetAvailable;

        if (_state.EvaluateCompletion(primary, observation.CompletionConditionsSatisfied,
                observation.IsTextEditTarget, observation.TextEditCommitted)
            == GuideStepReconciliationResult.Completed)
        {
            _completed = true;
            return Decide(GuideStepReconciliationResult.Completed, false, true);
        }

        if (!observation.ContextActive)
        {
            _state.ObserveContext(false);
            return Decide(GuideStepReconciliationResult.WaitingForContext, false, false);
        }

        if (!observation.TargetAvailable || !observation.TargetVisible)
        {
            _state.ObservePresentationAvailability(observation.TargetAvailable, observation.TargetVisible);
            return Decide(GuideStepReconciliationResult.WaitingForTarget, false, false);
        }

        if (!observation.PresentationStable)
        {
            _state.ObservePresentationStability(false);
            return Decide(GuideStepReconciliationResult.WaitingForTarget, false, false);
        }

        _state.ObservePresentationStability(true);
        var status = primary
            ? GuideStepReconciliationResult.WaitingForValidation
            : GuideStepReconciliationResult.WaitingForAction;
        return Decide(status, true, false);
    }

    /// <summary>Record that the platform completed the requested show/hide effect.</summary>
    public void AcknowledgePresentation(UnifiedPresentationAction action)
    {
        if (action == UnifiedPresentationAction.Show)
            _presentationVisible = true;
        else if (action == UnifiedPresentationAction.Hide)
            _presentationVisible = false;
    }

    private UnifiedStepDecision Decide(GuideStepReconciliationResult status, bool wantVisible, bool complete)
    {
        var action = wantVisible == _presentationVisible
            ? UnifiedPresentationAction.None
            : wantVisible ? UnifiedPresentationAction.Show : UnifiedPresentationAction.Hide;
        return new(status, action == UnifiedPresentationAction.Show,
            action == UnifiedPresentationAction.Hide, complete, action);
    }
}
