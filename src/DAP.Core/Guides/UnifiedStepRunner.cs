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
}
