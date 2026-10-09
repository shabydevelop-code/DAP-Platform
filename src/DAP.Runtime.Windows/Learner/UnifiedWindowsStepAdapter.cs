using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Windows.Bubbles;
using DAP.Runtime.Windows.Targets;
using DAP.Runtime.Windows.Validation;

namespace DAP.Runtime.Windows.Learner;

/// <summary>
/// Opt-in Windows UI Automation bridge for the new Core runner.
/// The production WindowsGuideRuntime is intentionally unchanged.
/// Click event subscriptions and completion-condition evaluation are not yet
/// implemented here; unsupported steps fail explicitly rather than auto-advance.
/// </summary>
public sealed class UnifiedWindowsStepAdapter : IUnifiedStepPlatformAdapter
{
    private readonly AutomationElement _windowRoot;
    private readonly WindowsTargetResolver _resolver;
    private readonly WindowsBubblePresenter _bubbles;
    private readonly WindowsValidationEvaluator _validation;
    private readonly int _number;
    private readonly int _total;
    private readonly TimeSpan _waitInterval;
    private AutomationElement? _target;
    private bool _targetPreviouslyResolved;

    public UnifiedWindowsStepAdapter(
        AutomationElement windowRoot, WindowsTargetResolver resolver,
        WindowsBubblePresenter bubbles, int number, int total,
        WindowsValidationEvaluator? validation = null, TimeSpan? waitInterval = null)
    {
        _windowRoot = windowRoot ?? throw new ArgumentNullException(nameof(windowRoot));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _validation = validation ?? new WindowsValidationEvaluator();
        _number = number;
        _total = total;
        _waitInterval = waitInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public Task<UnifiedStepObservation> ObserveAsync(GuideStep step, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (step.Target is null || step.Target.Runtime != TargetRuntime.Windows)
            throw new NotSupportedException("Unified Windows adapter requires a Windows target.");
        if (GuideStepExecutionPolicy.IsClickValidationStep(step)
            || step.CompletionConditions is { Count: > 0 })
            throw new NotSupportedException(
                "Unified Windows adapter has not yet implemented event-based or completion-condition validation.");

        try
        {
            var resolution = _resolver.Resolve(_windowRoot, step.Target);
            _target = resolution.Status == TargetResolutionStatus.Resolved ? resolution.Target : null;
            _targetPreviouslyResolved |= _target is not null;
            var visible = _target is not null && !_target.Current.IsOffscreen
                && !_target.Current.BoundingRectangle.IsEmpty;
            var primary = visible && step.Validation is not null
                && _validation.IsSatisfied(_target!, step.Validation);
            return Task.FromResult(new UnifiedStepObservation(
                true, _target is not null, visible, visible, primary, true,
                TargetWasPreviouslyAvailable: _targetPreviouslyResolved));
        }
        catch (ElementNotAvailableException)
        {
            _target = null;
            return Task.FromResult(new UnifiedStepObservation(true, false, false, false, false, false,
                TargetWasPreviouslyAvailable: _targetPreviouslyResolved));
        }
    }

    public async Task SetPresentationAsync(
        GuideStep step, UnifiedPresentationAction action, CancellationToken token)
    {
        if (action == UnifiedPresentationAction.Hide)
            await _bubbles.HideAsync();
        else if (action == UnifiedPresentationAction.Show)
        {
            if (_target is null)
                throw new InvalidOperationException("Cannot present a bubble without a resolved Windows target.");
            await _bubbles.ShowAsync(_target, step, _number, _total, token);
        }
    }

    public Task ApplyAutomationAsync(GuideStep step, string value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_target is null || !_target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new InvalidOperationException("Windows hybrid value target is unavailable.");
        ((ValuePattern)pattern).SetValue(value);
        return Task.CompletedTask;
    }

    public Task WaitForChangeAsync(
        GuideStep step, GuideStepReconciliationResult reason, CancellationToken token)
        => Task.Delay(_waitInterval, token);
}
