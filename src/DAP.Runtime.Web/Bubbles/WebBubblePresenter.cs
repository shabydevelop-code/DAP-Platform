using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Targets;
using Microsoft.Playwright;
using System.Diagnostics;

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

    public Task<TargetResolution<ILocator>> ShowAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken = default)
        => PresentAsync(page, step, ensureOnly: false, cancellationToken);

    public Task<TargetResolution<ILocator>> EnsureShownAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken = default)
        => PresentAsync(page, step, ensureOnly: true, cancellationToken);

    private async Task<TargetResolution<ILocator>> PresentAsync(
        IPage page,
        GuideStep step,
        bool ensureOnly,
        CancellationToken cancellationToken)
    {
        if (step.Target is null)
            return TargetResolution<ILocator>.NotFound();

        var timing = Stopwatch.StartNew();
        var resolution = await _targets.ResolveAsync(page, step.Target, cancellationToken);
        var resolvedAt = timing.Elapsed.TotalMilliseconds;
        if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
            return resolution;

        const string script = """
(el, b) => {
    const root = el.ownerDocument;

    // Event-based validation must be armed before the user can act on the
    // instruction. Installing this only in the later validation poll creates
    // a race where a fast click is lost.
    if (b.validationKind === 'clicked' && !el.__dapValidationClickInstalled) {
        el.__dapValidationClickInstalled = true;
        el.__dapValidationClicked = false;
        el.addEventListener('click', () => { el.__dapValidationClicked = true; }, { capture: true });
    }

    const existing = root.getElementById('dap-guide-bubble');
    if (b.ensureOnly && existing?.__dapTarget === el && existing?.dataset.dapStepId === b.stepId)
        return;

    existing?.__dapCleanup?.();
    existing?.remove();
    const previousHighlight = {
        outline: el.style.outline,
        outlineOffset: el.style.outlineOffset,
        boxShadow: el.style.boxShadow
    };
    el.style.outline = b.theme.targetHighlightWidth + 'px solid ' + b.theme.targetHighlightColor;
    el.style.outlineOffset = '0px';
    el.style.boxShadow = b.theme.targetHighlightShadow;

    const bubble = root.createElement('div');
    bubble.id = 'dap-guide-bubble';
    bubble.setAttribute('role', 'status');
    bubble.dataset.placement = b.placement;
    bubble.dataset.dapStepId = b.stepId;
    bubble.__dapTarget = el;

    const content = root.createElement('div');
    content.textContent = b.content;
    bubble.appendChild(content);

    const pointer = root.createElement('div');
    pointer.setAttribute('aria-hidden', 'true');
    pointer.dataset.dapPointer = '1';
    Object.assign(pointer.style, {
        position: 'absolute',
        width: '0',
        height: '0'
    });
    bubble.appendChild(pointer);

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

    const placePointer = (side) => {
        const s = b.theme.pointerSize;
        pointer.style.left = '';
        pointer.style.right = '';
        pointer.style.top = '';
        pointer.style.bottom = '';
        pointer.style.transform = '';
        pointer.style.borderLeft = s + 'px solid transparent';
        pointer.style.borderRight = s + 'px solid transparent';
        pointer.style.borderTop = s + 'px solid transparent';
        pointer.style.borderBottom = s + 'px solid transparent';

        if (side === 'Top') {
            pointer.style.left = '50%';
            pointer.style.bottom = (-2 * s) + 'px';
            pointer.style.transform = 'translateX(-50%)';
            pointer.style.borderTopColor = b.theme.backgroundColor;
        } else if (side === 'Bottom') {
            pointer.style.left = '50%';
            pointer.style.top = (-2 * s) + 'px';
            pointer.style.transform = 'translateX(-50%)';
            pointer.style.borderBottomColor = b.theme.backgroundColor;
        } else if (side === 'Left') {
            pointer.style.top = '50%';
            pointer.style.right = (-2 * s) + 'px';
            pointer.style.transform = 'translateY(-50%)';
            pointer.style.borderLeftColor = b.theme.backgroundColor;
        } else {
            pointer.style.top = '50%';
            pointer.style.left = (-2 * s) + 'px';
            pointer.style.transform = 'translateY(-50%)';
            pointer.style.borderRightColor = b.theme.backgroundColor;
        }
    };

    const place = () => {
        const r = el.getBoundingClientRect();
        const q = bubble.getBoundingClientRect();
        const gap = b.theme.pointerSize + 8;
        let side = b.placement === 'Auto' ? 'Bottom' : b.placement;
        let x;
        let y;

        const coords = (candidate) => {
            if (candidate === 'Top')
                return [r.left + (r.width - q.width) / 2, r.top - q.height - gap];
            if (candidate === 'Left')
                return [r.left - q.width - gap, r.top + (r.height - q.height) / 2];
            if (candidate === 'Right')
                return [r.right + gap, r.top + (r.height - q.height) / 2];
            return [r.left + (r.width - q.width) / 2, r.bottom + gap];
        };

        [x, y] = coords(side);

        if (b.placement === 'Auto') {
            const fitsBottom = y + q.height <= innerHeight - 8;
            if (!fitsBottom) {
                const [tx, ty] = coords('Top');
                if (ty >= 8) {
                    side = 'Top';
                    x = tx;
                    y = ty;
                }
            }
        }

        x = Math.max(8, Math.min(x, innerWidth - q.width - 8));
        y = Math.max(8, Math.min(y, innerHeight - q.height - 8));
        bubble.style.left = x + 'px';
        bubble.style.top = y + 'px';
        bubble.dataset.actualPlacement = side;
        placePointer(side);
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
        el.style.outline = previousHighlight.outline;
        el.style.outlineOffset = previousHighlight.outlineOffset;
        el.style.boxShadow = previousHighlight.boxShadow;
    };
}
""";

        await resolution.Target.EvaluateAsync(
            script,
            new
            {
                content = step.Bubble.Content,
                placement = step.Bubble.Placement.ToString(),
                stepId = step.Id,
                ensureOnly,
                validationKind = step.Validation?.Kind,
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
                    direction = _theme.Direction,
                    targetHighlightColor = _theme.TargetHighlightColor,
                    targetHighlightWidth = _theme.TargetHighlightWidth,
                    targetHighlightShadow = _theme.TargetHighlightShadow,
                    pointerSize = _theme.PointerSize
                }
            });
        var presentedAt = timing.Elapsed.TotalMilliseconds;
        if (!ensureOnly)
            Console.Error.WriteLine($"[DAP bubble] Step '{step.Id}' target resolved in {resolvedAt:F0} ms; DOM presentation completed in {presentedAt:F0} ms.");
        else
            Console.Error.WriteLine($"[DAP bubble] Step '{step.Id}' ensure presentation: resolve {resolvedAt:F0} ms; total {presentedAt:F0} ms.");

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
