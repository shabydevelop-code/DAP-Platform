namespace DAP.Core.Guides;

/// <summary>Platform-neutral active-step observation loop.</summary>
public sealed class UnifiedGuideStepEngine
{
    /// <summary>Evaluate active context through the shared step state; presentation effects stay platform-specific.</summary>
    public GuideStepReconciliationResult ObserveContext(GuideActiveStepState state, bool contextActive)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ObserveContext(contextActive);
    }

    /// <summary>Centralize context and target readiness decisions without rendering effects.</summary>
    public GuideStepReconciliationResult ObserveReadiness(
        GuideActiveStepState state, bool contextActive, bool targetAvailable, bool targetVisible)
    {
        ArgumentNullException.ThrowIfNull(state);
        var context = ObserveContext(state, contextActive);
        if (context == GuideStepReconciliationResult.WaitingForContext)
            return context;
        return state.ObservePresentationAvailability(targetAvailable, targetVisible);
    }

    /// <summary>Apply the shared completion policy to observations from either adapter.</summary>
    public GuideStepReconciliationResult EvaluateCompletion(
        GuideActiveStepState state, bool primarySatisfied, bool conditionsSatisfied,
        bool isTextEditTarget = false, bool textEditCommitted = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.EvaluateCompletion(primarySatisfied, conditionsSatisfied, isTextEditTarget, textEditCommitted);
    }

    public async Task RunAsync(GuideActiveStepState state, Func<GuideActiveStepState, CancellationToken, Task<GuideStepReconciliationResult>> observeAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observeAsync);
        // Platform callbacks observe targets and perform effects; the shared engine
        // owns the observation lifecycle and validates every reported transition.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await observeAsync(state, cancellationToken);
            if (!Enum.IsDefined(result))
                throw new InvalidOperationException($"Unknown reconciliation result: {result}.");
            if (result == GuideStepReconciliationResult.Completed)
                return;
        }
    }
}
