using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Bubbles;

public sealed class WebBubblePresenter
{
    private readonly WebTargetResolver _targets;
    private readonly WebBubbleTheme _theme;

    public WebBubblePresenter(WebTargetResolver targets, WebBubbleTheme? theme = null)
    {
        _targets = targets;
        _theme = theme ?? WebBubbleTheme.Default;
    }

    public async Task<TargetResolution<ILocator>> ShowAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken = default)
    {
        if (step.Target is null)
            return TargetResolution<ILocator>.NotFound();

        var resolution = await _targets.ResolveAsync(page, step.Target, cancellationToken);
        if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
            return resolution;

        const string script = """
(el, b) => {
    const root = el.ownerDocument;
    const existing = root.getElementById('dap-guide-bubble');
    existing?.__dapCleanup?.();
    existing?.remove();

    const bubble = root.createElement('div');
    bubble.id = 'dap-guide-bubble';
    bubble.setAttribute('role', 'status');
    bubble.dataset.placement = b.placement;
    bubble.textContent = b.content;

    Object.assign(bubble.style, {
        position: 'fixed',
        zIndex: '2147483646',
        maxWidth: b.theme.maxWidth + 'px',
        padding: b.theme.padding,
        background: b.theme.backgroundColor,
        color: b.theme.textColor,
        border: b.theme.borderWidth + 'px solid ' + b.theme.borderColor,
        borderRadius: b.theme.borderRadius + 'px',
        boxShadow: b.theme.boxShadow,
        fontFamily: b.theme.fontFamily,
        fontSize: b.theme.fontSize + 'px',
        lineHeight: String(b.theme.lineHeight),
        direction: b.theme.direction
    });

    root.body.appendChild(bubble);

    const place = () => {
        const r = el.getBoundingClientRect();
        const q = bubble.getBoundingClientRect();
        const gap = 10;
        let x = r.right + gap;
        let y = r.top + (r.height - q.height) / 2;

        if (b.placement === 'Top') {
            x = r.left + (r.width - q.width) / 2;
            y = r.top - q.height - gap;
        } else if (b.placement === 'Bottom' || b.placement === 'Auto') {
            x = r.left + (r.width - q.width) / 2;
            y = r.bottom + gap;
        } else if (b.placement === 'Left') {
            x = r.left - q.width - gap;
            y = r.top + (r.height - q.height) / 2;
        }

        x = Math.max(8, Math.min(x, innerWidth - q.width - 8));
        y = Math.max(8, Math.min(y, innerHeight - q.height - 8));
        bubble.style.left = x + 'px';
        bubble.style.top = y + 'px';
    };

    place();
    const ro = new ResizeObserver(place);
    ro.observe(el);
    ro.observe(bubble);
    root.defaultView.addEventListener('scroll', place, true);
    root.defaultView.addEventListener('resize', place);

    bubble.__dapCleanup = () => {
        ro.disconnect();
        root.defaultView.removeEventListener('scroll', place, true);
        root.defaultView.removeEventListener('resize', place);
    };
}
""";

        await resolution.Target.EvaluateAsync(
            script,
            new
            {
                content = step.Bubble.Content,
                placement = step.Bubble.Placement.ToString(),
                theme = new
                {
                    backgroundColor = _theme.BackgroundColor,
                    textColor = _theme.TextColor,
                    borderColor = _theme.BorderColor,
                    borderWidth = _theme.BorderWidth,
                    borderRadius = _theme.BorderRadius,
                    maxWidth = _theme.MaxWidth,
                    padding = _theme.Padding,
                    boxShadow = _theme.BoxShadow,
                    fontFamily = _theme.FontFamily,
                    fontSize = _theme.FontSize,
                    lineHeight = _theme.LineHeight,
                    direction = _theme.Direction
                }
            });

        return resolution;
    }

    public async Task HideAsync(IPage page)
    {
        const string script = """
() => {
    const bubble = document.getElementById('dap-guide-bubble');
    if (bubble) {
        bubble.__dapCleanup?.();
        bubble.remove();
    }
}
""";

        foreach (var frame in page.Frames)
        {
            try
            {
                await frame.EvaluateAsync(script);
            }
            catch (PlaywrightException)
            {
                // A frame can disappear while cleanup is running.
            }
        }
    }
}
