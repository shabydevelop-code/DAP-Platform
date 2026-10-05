using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Bubbles;
using DAP.Runtime.Web.Learner;
using DAP.Runtime.Web.Validation;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Browser;

/// <summary>
/// Compatibility adapter over the proven Playwright implementation.
/// This class contains no guide sequencing policy; it exposes browser facts and
/// presentation/event primitives through IWebBrowserAdapter.
/// </summary>
public sealed class PlaywrightWebBrowserAdapter : IWebBrowserAdapter
{
    private readonly IPage _page;
    private readonly WebBubblePresenter _bubbles;
    private readonly WebStepContextGuard _context;
    private readonly WebValidationEvaluator _validation;
    private readonly WebValidationSession _session;
    private readonly WebCompletionConditionEvaluator _completion = new();

    public PlaywrightWebBrowserAdapter(
        IPage page,
        WebBubblePresenter bubbles,
        WebStepContextGuard? context = null,
        WebValidationEvaluator? validation = null,
        WebValidationSession? session = null)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _context = context ?? new WebStepContextGuard();
        _validation = validation ?? new WebValidationEvaluator();
        _session = session ?? new WebValidationSession();
    }

    public async Task<WebTargetResolution> ResolveTargetAsync(TargetDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var step = new GuideStep("__resolve__", 0, descriptor, new BubbleDefinition(""));
        var r = await _bubbles.ResolveTargetAsync(_page, step, cancellationToken);
        return new(Map(r.Status), r.CandidateCount);
    }

    public Task<bool> IsContextActiveAsync(GuideStep step, CancellationToken cancellationToken = default)
        => _context.IsActiveAsync(_page, step, cancellationToken);

    public async Task<bool> IsStableForPresentationAsync(GuideStep step, TimeSpan quietWindow, CancellationToken cancellationToken = default)
    {
        var r = await _bubbles.ResolveTargetAsync(_page, step, cancellationToken);
        if (r.Status != TargetResolutionStatus.Resolved || r.Target is null) return false;
        return await r.Target.EvaluateAsync<bool>(
            @"(el, settleMs) => new Promise(resolve => {
                if (!el.isConnected) { resolve(false); return; }
                const doc=el.ownerDocument; let timer;
                const finish=()=>{observer.disconnect();const r=el.getBoundingClientRect();resolve(el.isConnected&&r.width>0&&r.height>0);};
                const reset=()=>{clearTimeout(timer);timer=setTimeout(finish,settleMs);};
                const observer=new MutationObserver(reset);
                observer.observe(doc.documentElement,{subtree:true,childList:true,attributes:true,characterData:true});
                reset();
            })", quietWindow.TotalMilliseconds);
    }

    public async Task<WebValidationCommit?> WaitForValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        await _session.EnsureBridgeAsync(_page);
        await _session.WaitForCompletionAsync(step.Id).WaitAsync(cancellationToken);
        return new(step.Id, step.Validation?.Kind ?? "");
    }

    public Task ConsumeValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        _session.ConsumeCompletion(step.Id);
        return Task.CompletedTask;
    }

    public async Task<bool> IsPrimaryValidationSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Validation is null) return false;
        if (string.Equals(step.Validation.Kind, "clicked", StringComparison.Ordinal)) return _session.IsCompleted(step.Id);
        var r = await _bubbles.ResolveTargetAsync(_page, step, cancellationToken);
        return r.Status == TargetResolutionStatus.Resolved && r.Target is not null
            && await _validation.IsSatisfiedAsync(r.Target, step.Validation, cancellationToken);
    }

    public Task<bool> AreCompletionConditionsSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default)
        => _completion.AreSatisfiedAsync(_page, step, cancellationToken);

    public async Task<WebBubblePresentation> EnsureBubbleShownAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default)
    {
        var r = await _bubbles.EnsureShownAsync(_page, step, stepNumber, totalSteps, cancellationToken);
        return new(Map(r.Status), r.CandidateCount);
    }

    public Task HideBubbleAsync(CancellationToken cancellationToken = default)
        => _bubbles.HideAsync(_page);

    public Task WaitForCenteredStepDismissalAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default)
        => _bubbles.WaitForCenteredStepDismissalAsync(_page, step, stepNumber, totalSteps, cancellationToken);

    public Task WaitForGuideCompletedDismissalAsync(CancellationToken cancellationToken = default)
        => _bubbles.WaitForGuideCompletedDismissalAsync(_page, cancellationToken);

    public Task<string?> CaptureAsync(GuideStep step, CancellationToken cancellationToken = default)
        => WebGuideRuntime.CaptureStepValueAsync(_page, step, cancellationToken);

    private static WebTargetResolutionStatus Map(TargetResolutionStatus status) => status switch
    {
        TargetResolutionStatus.Resolved => WebTargetResolutionStatus.Resolved,
        TargetResolutionStatus.Ambiguous => WebTargetResolutionStatus.Ambiguous,
        _ => WebTargetResolutionStatus.NotFound
    };
}
