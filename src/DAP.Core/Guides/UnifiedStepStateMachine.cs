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

public enum UnifiedLearnerMode { Manual, Hybrid }

/// <summary>Mode is selected by the host; only persisted automation values are eligible.</summary>
public sealed record UnifiedStepOptions(UnifiedLearnerMode Mode)
{
    public bool CanApplyAutomation(GuideStep step, bool targetResolved, bool alreadyApplied)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Mode == UnifiedLearnerMode.Hybrid && targetResolved
            && !alreadyApplied && !string.IsNullOrEmpty(step.AutomationValue);
    }
}

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
    private bool _textEditCommitted;
    private bool _targetWasStable;

    public UnifiedStepStateMachine(GuideStep step)
    {
        _step = step ?? throw new ArgumentNullException(nameof(step));
        _state = new GuideActiveStepState(step);
    }

    public bool Completed => _completed;
    public bool HybridValueApplied => _state.HybridValueApplied;

    public void MarkHybridValueApplied() => _state.MarkHybridValueApplied();

    /// <summary>Mark a single persisted hybrid action as executed; never repeat it.</summary>
    public bool TryApplyHybridValue(UnifiedStepOptions options, bool targetResolved)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.CanApplyAutomation(_step, targetResolved, _state.HybridValueApplied))
            return false;
        _state.MarkHybridValueApplied();
        return true;
    }

    public UnifiedStepDecision Advance(UnifiedStepObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (_completed)
            return new(GuideStepReconciliationResult.Completed, false, false, true);

        _targetEverAvailable |= observation.TargetAvailable || observation.TargetWasPreviouslyAvailable;
        _actionLatched |= observation.ActionObserved;
        _textEditCommitted |= observation.TextEditCommitted;

        // A click or disappearance can complete after navigation has retired its
        // source context. Check the latched action before target readiness.
        bool primary = observation.PrimarySatisfied;
        if (GuideStepExecutionPolicy.IsClickValidationStep(_step))
            primary |= _actionLatched;
        if (GuideStepExecutionPolicy.IsTargetDisappearanceStep(_step))
            primary |= _targetEverAvailable && !observation.TargetAvailable;

        if (_state.EvaluateCompletion(primary, observation.CompletionConditionsSatisfied,
                observation.IsTextEditTarget, _textEditCommitted)
            == GuideStepReconciliationResult.Completed)
        {
            _completed = true;
            return Decide(GuideStepReconciliationResult.Completed, false, true);
        }

        if (!observation.ContextActive)
        {
            _targetWasStable = false;
            _state.ObserveContext(false);
            return Decide(GuideStepReconciliationResult.WaitingForContext, false, false);
        }

        if (!observation.TargetAvailable || !observation.TargetVisible)
        {
            _targetWasStable = false;
            _state.ObservePresentationAvailability(observation.TargetAvailable, observation.TargetVisible);
            return Decide(GuideStepReconciliationResult.WaitingForTarget, false, false);
        }

        if (!observation.PresentationStable)
        {
            _state.ObservePresentationStability(false);
            // A temporary unstable layout must not flash an already visible bubble.
            // A missing or invisible target is handled separately above.
            return Decide(GuideStepReconciliationResult.WaitingForTarget, _targetWasStable, false);
        }

        _targetWasStable = true;
        _state.ObservePresentationStability(true);
        var status = primary
            ? GuideStepReconciliationResult.WaitingForValidation
            : GuideStepReconciliationResult.WaitingForAction;
        return Decide(status, true, false);
    }

    /// <summary>
    /// Drive observations and presentation effects without polling inside the core.
    /// The adapter controls its event source and supplies each subsequent observation.
    /// </summary>
    public async Task RunAsync(
        Func<CancellationToken, Task<UnifiedStepObservation>> observeAsync,
        Func<UnifiedPresentationAction, CancellationToken, Task> presentAsync,
        Func<GuideStepReconciliationResult, CancellationToken, Task> waitAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observeAsync);
        ArgumentNullException.ThrowIfNull(presentAsync);
        ArgumentNullException.ThrowIfNull(waitAsync);

        while (!Completed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = Advance(await observeAsync(cancellationToken));
            if (decision.PresentationAction != UnifiedPresentationAction.None)
            {
                await presentAsync(decision.PresentationAction, cancellationToken);
                AcknowledgePresentation(decision.PresentationAction);
            }

            if (decision.StepCompleted)
                return;

            await waitAsync(decision.Status, cancellationToken);
        }
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
