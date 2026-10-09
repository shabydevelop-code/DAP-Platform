namespace DAP.Core.Guides;

/// <summary>Platform-neutral active-step observation loop.</summary>
public sealed class UnifiedGuideStepEngine
{
    /// <summary>Centralize context and target readiness decisions without rendering effects.</summary>
    public GuideStepReconciliationResult ObserveReadiness(
        GuideActiveStepState state, bool contextActive, bool targetAvailable, bool targetVisible)
    {
        ArgumentNullException.ThrowIfNull(state);
        var context = state.ObserveContext(contextActive);
        if (context == GuideStepReconciliationResult.WaitingForContext)
            return context;
        return state.ObservePresentationAvailability(targetAvailable, targetVisible);
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
