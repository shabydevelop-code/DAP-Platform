using DAP.Core.Guides;
using DAP.Runtime.Web.Browser;

namespace DAP.Runtime.Web.Learner;

/// <summary>
/// Opt-in bridge from the real extension-backed browser boundary to the new
/// Core runner. It is not enabled by the existing guide launcher yet.
/// </summary>
public sealed class UnifiedWebStepAdapter : IUnifiedStepPlatformAdapter
{
    private readonly IWebBrowserAdapter _browser;
    private readonly int _number;
    private readonly int _total;
    private readonly TimeSpan _quietWindow;
    private readonly TimeSpan _waitInterval;
    private Task<WebValidationCommit?>? _commit;
    private bool _armed;
    private bool _clickLatched;
    private bool _valueLatched;
    private bool _previouslyResolved;

    public UnifiedWebStepAdapter(
        IWebBrowserAdapter browser, int number, int total,
        TimeSpan? quietWindow = null, TimeSpan? waitInterval = null)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _number = number;
        _total = total;
        _quietWindow = quietWindow ?? TimeSpan.FromMilliseconds(250);
        _waitInterval = waitInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task<UnifiedStepObservation> ObserveAsync(GuideStep step, CancellationToken token)
    {
        if (_commit is { IsCompletedSuccessfully: true } && _commit.Result is not null)
        {
            if (GuideStepExecutionPolicy.IsClickValidationStep(step))
                _clickLatched = true;
            else if (!_valueLatched)
            {
                _valueLatched = await _browser.IsPrimaryValidationSatisfiedAsync(step, token);
                if (!_valueLatched)
                {
                    await _browser.ConsumeValidationCommitAsync(step, token);
                    _commit = _browser.WaitForArmedValidationCommitAsync(step, token);
                }
            }
        }
        var context = await _browser.IsContextActiveAsync(step, token);
        if (!context)
            return new(false, false, false, false,
                _clickLatched || _valueLatched,
                await _browser.AreCompletionConditionsSatisfiedAsync(step, token),
                ActionObserved: _clickLatched,
                TargetWasPreviouslyAvailable: _previouslyResolved);

        var resolution = step.Target is null
            ? new WebTargetResolution(WebTargetResolutionStatus.Resolved)
            : await _browser.ResolveTargetAsync(step.Target, token);
        var resolved = resolution.Status == WebTargetResolutionStatus.Resolved;
        _previouslyResolved |= resolved;
        if (resolved && !_armed && GuideStepExecutionPolicy.IsAutomaticValidationStep(step))
        {
            await _browser.ArmValidationAsync(step, token);
            _commit = _browser.WaitForArmedValidationCommitAsync(step, token);
            _armed = true;
        }

        var committed = _commit?.IsCompletedSuccessfully == true && _commit.Result is not null;
        var primary = _clickLatched || _valueLatched;
        if (committed && !primary)
            primary = await _browser.IsPrimaryValidationSatisfiedAsync(step, token);
        var conditions = await _browser.AreCompletionConditionsSatisfiedAsync(step, token);
        var stable = resolved && await _browser.IsStableForPresentationAsync(step, _quietWindow, token);
        return new(true, resolved, resolved, stable, primary, conditions,
            ActionObserved: _clickLatched,
            TargetWasPreviouslyAvailable: _previouslyResolved);
    }

    public async Task SetPresentationAsync(GuideStep step, UnifiedPresentationAction action, CancellationToken token)
    {
        if (action == UnifiedPresentationAction.Show)
            await _browser.EnsureBubbleShownAsync(step, _number, _total, true, token);
        else if (action == UnifiedPresentationAction.Hide)
            await _browser.HideBubbleAsync(token);
    }

    public Task ApplyAutomationAsync(GuideStep step, string value, CancellationToken token)
        => _browser.ApplyAutomationValueAsync(step, token);

    public async Task WaitForChangeAsync(GuideStep step, GuideStepReconciliationResult reason, CancellationToken token)
    {
        // Wait for an armed commit when available, otherwise periodically
        // reconcile application context and asynchronous DOM changes.
        if (_commit is { IsCompleted: false })
            await Task.WhenAny(_commit, Task.Delay(_waitInterval, token));
        else
            await Task.Delay(_waitInterval, token);
        token.ThrowIfCancellationRequested();
    }
}
