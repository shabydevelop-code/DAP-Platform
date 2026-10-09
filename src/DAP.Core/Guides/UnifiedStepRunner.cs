namespace DAP.Core.Guides;

/// <summary>
/// Platform boundary for the new step state machine. Web and Windows implement
/// observations, visual effects, persisted hybrid actions and event-driven waiting.
/// The core never touches DOM or UI Automation objects.
/// </summary>
public interface IUnifiedStepPlatformAdapter
{
    Task<UnifiedStepObservation> ObserveAsync(GuideStep step, CancellationToken cancellationToken);
    Task SetPresentationAsync(GuideStep step, UnifiedPresentationAction action, CancellationToken cancellationToken);
    Task ApplyAutomationAsync(GuideStep step, string value, CancellationToken cancellationToken);
    Task WaitForChangeAsync(GuideStep step, GuideStepReconciliationResult reason, CancellationToken cancellationToken);
}

/// <summary>
/// A thin bridge for either runtime. Each delegate is implemented by Web or Windows;
/// no platform-specific condition or timing rule is encoded in this adapter.
/// </summary>
public sealed class DelegateUnifiedStepPlatformAdapter : IUnifiedStepPlatformAdapter
{
    private readonly Func<GuideStep, CancellationToken, Task<UnifiedStepObservation>> _observe;
    private readonly Func<GuideStep, UnifiedPresentationAction, CancellationToken, Task> _present;
    private readonly Func<GuideStep, string, CancellationToken, Task> _automate;
    private readonly Func<GuideStep, GuideStepReconciliationResult, CancellationToken, Task> _wait;

    public DelegateUnifiedStepPlatformAdapter(
        Func<GuideStep, CancellationToken, Task<UnifiedStepObservation>> observe,
        Func<GuideStep, UnifiedPresentationAction, CancellationToken, Task> present,
        Func<GuideStep, string, CancellationToken, Task> automate,
        Func<GuideStep, GuideStepReconciliationResult, CancellationToken, Task> wait)
    {
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _automate = automate ?? throw new ArgumentNullException(nameof(automate));
        _wait = wait ?? throw new ArgumentNullException(nameof(wait));
    }

    public Task<UnifiedStepObservation> ObserveAsync(GuideStep step, CancellationToken token)
        => _observe(step, token);
    public Task SetPresentationAsync(GuideStep step, UnifiedPresentationAction action, CancellationToken token)
        => _present(step, action, token);
    public Task ApplyAutomationAsync(GuideStep step, string value, CancellationToken token)
        => _automate(step, value, token);
    public Task WaitForChangeAsync(GuideStep step, GuideStepReconciliationResult reason, CancellationToken token)
        => _wait(step, reason, token);
}

/// <summary>Runs one step against a platform adapter without platform-specific branches.</summary>
public sealed class UnifiedStepRunner
{
    public async Task RunAsync(
        GuideStep step, UnifiedStepOptions options, IUnifiedStepPlatformAdapter adapter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(adapter);

        var machine = new UnifiedStepStateMachine(step);
        try
        {
            await machine.RunAsync(
            async token =>
            {
                var observation = await adapter.ObserveAsync(step, token);
                if (options.CanApplyAutomation(step,
                        observation.ContextActive && observation.TargetAvailable,
                        machine.HybridValueApplied))
                {
                    // Mark only after successful platform execution so a transient
                    // adapter failure cannot silently consume the automation action.
                    await adapter.ApplyAutomationAsync(step, step.AutomationValue!, token);
                    machine.MarkHybridValueApplied();
                    observation = await adapter.ObserveAsync(step, token);
                }
                return observation;
            },
            (action, token) => adapter.SetPresentationAsync(step, action, token),
            (reason, token) => adapter.WaitForChangeAsync(step, reason, token),
            cancellationToken);
        }
        catch
        {
            // Never leave a stale guide bubble after cancellation or adapter failure.
            // Cleanup must not mask the original exception.
            try
            {
                await adapter.SetPresentationAsync(step, UnifiedPresentationAction.Hide,
                    CancellationToken.None);
            }
            catch { /* Best-effort cleanup; preserve the original failure. */ }
            throw;
        }
    }
}
