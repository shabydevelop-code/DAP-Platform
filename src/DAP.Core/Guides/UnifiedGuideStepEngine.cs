namespace DAP.Core.Guides;

/// <summary>Platform-neutral active-step observation loop.</summary>
public sealed class UnifiedGuideStepEngine
{
    public async Task RunAsync(GuideActiveStepState state, Func<GuideActiveStepState, CancellationToken, Task<GuideStepReconciliationResult>> observeAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observeAsync);
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
