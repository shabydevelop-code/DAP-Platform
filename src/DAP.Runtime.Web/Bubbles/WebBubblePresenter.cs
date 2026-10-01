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
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken = default)
        => PresentAsync(page, step, ensureOnly: true, cancellationToken, stepNumber, totalSteps);

    private async Task<TargetResolution<ILocator>> PresentAsync(
        IPage page,
        GuideStep step,
        bool ensureOnly,
        CancellationToken cancellationToken,
        int? stepNumber = null,
        int? totalSteps = null)
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

    // Event completion is owned by DAP.exe, not by this document. The
    // Playwright binding survives navigation/document replacement, so a click
    // that starts a server round trip cannot be forgotten when this DOM dies.
    if (b.validationKind === 'clicked') {
        if (el.__dapValidationClickHandler)
            el.removeEventListener('click', el.__dapValidationClickHandler, true);
        const clickHandler = event => {
            const activeBubble = root.getElementById('dap-guide-bubble');
            if (activeBubble?.dataset.dapStepId === b.stepId) {
                activeBubble.__dapCleanup?.();
                activeBubble.remove();
            }

            const report = root.defaultView?.__dapReportValidation;
            if (typeof report !== 'function')
                return;

            // A click may synchronously/quickly start navigation or document
            // replacement. A fire-and-forget Playwright binding call can be
            // destroyed with that document before DAP.exe receives it. For
            // navigation-capable clicks, hold the browser's default action only
            // until DAP acknowledges the completion event, then replay it.
            //
            // Programmatic application handlers still run for this click. The
            // guard is therefore used only when the browser itself has a
            // cancelable default navigation/submission action to defer.
            const tag = el.tagName?.toLowerCase();
            const type = (el.getAttribute?.('type') || '').toLowerCase();
            const form = el.form;
            const defersDefault =
                event.cancelable &&
                ((tag === 'a' && !!el.getAttribute('href')) ||
                 (tag === 'button' && form && (!type || type === 'submit')) ||
                 (tag === 'input' && form && (type === 'submit' || type === 'image')));

            if (!defersDefault) {
                void report(b.stepId);
                return;
            }

            event.preventDefault();
            void report(b.stepId).then(() => {
                if (!el.isConnected)
                    return;
                if (tag === 'a') {
                    const href = el.getAttribute('href');
                    if (href) root.defaultView.location.href = href;
                    return;
                }
                if (form)
                    form.requestSubmit(el);
            });
        };
        el.__dapValidationClickHandler = clickHandler;
        el.addEventListener('click', clickHandler, { capture: true });
    }

    // Value validations complete on the interaction's natural commit event,
    // not merely when polling first observes a non-empty/intermediate value.
    // Text editing commits on blur after a real edit; discrete controls commit
    // on change. DAP.exe remains the owner of completion state.
    if (b.validationKind && b.validationKind !== 'clicked') {
        // A live DOM control can participate in multiple Guide Steps. The
        // validation listener therefore belongs to the active Step, not merely
        // to the element. Replace the prior Step listener when the same element
        // is reused later in the Guide.
        if (el.__dapValidationCommitHandler)
            el.removeEventListener(el.__dapValidationCommitEvent, el.__dapValidationCommitHandler, true);
        if (el.__dapValidationInputHandler)
            el.removeEventListener('input', el.__dapValidationInputHandler, true);
        if (el.__dapValidationChangeHandler)
            el.removeEventListener('change', el.__dapValidationChangeHandler, true);
        const tag = el.tagName?.toLowerCase();
        const type = (el.getAttribute?.('type') || '').toLowerCase();
        const isTextEditor = tag === 'textarea' ||
            (tag === 'input' && !['checkbox','radio','button','submit','reset'].includes(type));
        const eventName = isTextEditor ? 'blur' : 'change';
        let changed = false;

        if (isTextEditor) {
            const markChanged = () => { changed = true; };
            el.__dapValidationInputHandler = markChanged;
            el.__dapValidationChangeHandler = markChanged;
            el.addEventListener('input', markChanged, { capture: true });
            el.addEventListener('change', markChanged, { capture: true });
        }

        const commit = () => {
            if (isTextEditor && !changed)
                return;

            const report = root.defaultView?.__dapReportValidation;
            if (typeof report === 'function')
                void report(b.stepId);
        };
        el.__dapValidationCommitHandler = commit;
        el.__dapValidationCommitEvent = eventName;
        el.addEventListener(eventName, commit, { capture: true });
    }

    const existing = root.getElementById('dap-guide-bubble');

    if (b.ensureOnly && existing?.__dapTarget === el && existing?.dataset.dapStepId === b.stepId)
        return;

    // A newly resolved Step can legitimately target an element below/above the
    // current viewport. Bring the target into view before creating/placing its
    // bubble. Do this only for a new presentation: reconciliation must not keep
    // forcing the learner back if they intentionally scroll while the Step is active.
    const initialRect = el.getBoundingClientRect();
    const initiallyInViewport =
        initialRect.width > 0 &&
        initialRect.height > 0 &&
        initialRect.bottom > 0 &&
        initialRect.right > 0 &&
        initialRect.top < root.defaultView.innerHeight &&
        initialRect.left < root.defaultView.innerWidth;
    if (!initiallyInViewport)
        el.scrollIntoView({ behavior: 'auto', block: 'center', inline: 'nearest' });

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

    if (b.stepNumber && b.totalSteps) {
        const progress = root.createElement('div');
        progress.textContent = 'שלב ' + b.stepNumber + ' מתוך ' + b.totalSteps;
        Object.assign(progress.style, {
            fontSize: '12px',
            opacity: '0.78',
            marginBottom: '5px',
            fontWeight: '600'
        });
        bubble.appendChild(progress);
    }

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
        direction: b.theme.direction,
        visibility: 'hidden',
        cursor: 'grab',
        touchAction: 'none',
        userSelect: 'none'
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

    let manuallyPositioned = false;
    let dragState = null;
    const margin = 8;

    const clampManualPosition = (x, y) => {
        const q = bubble.getBoundingClientRect();
        const viewportWidth = root.defaultView.innerWidth;
        const viewportHeight = root.defaultView.innerHeight;
        return {
            x: Math.max(margin, Math.min(x, Math.max(margin, viewportWidth - q.width - margin))),
            y: Math.max(margin, Math.min(y, Math.max(margin, viewportHeight - q.height - margin)))
        };
    };

    const keepManualPositionInViewport = () => {
        if (!manuallyPositioned)
            return;
        const q = bubble.getBoundingClientRect();
        const next = clampManualPosition(q.left, q.top);
        bubble.style.left = next.x + 'px';
        bubble.style.top = next.y + 'px';
        bubble.style.visibility = 'visible';
    };

    const onPointerMove = (event) => {
        if (!dragState || event.pointerId !== dragState.pointerId)
            return;
        const next = clampManualPosition(
            dragState.left + event.clientX - dragState.clientX,
            dragState.top + event.clientY - dragState.clientY);
        bubble.style.left = next.x + 'px';
        bubble.style.top = next.y + 'px';
    };

    const finishDrag = (event) => {
        if (!dragState || event.pointerId !== dragState.pointerId)
            return;
        manuallyPositioned = true;
        dragState = null;
        bubble.style.cursor = 'grab';
        pointer.style.display = 'none';
        bubble.dataset.manualPosition = 'true';
        try { bubble.releasePointerCapture(event.pointerId); } catch { }
    };

    const onPointerDown = (event) => {
        // Preserve normal interaction if future bubble content contains an
        // actual interactive control.
        if (event.button !== 0 || event.target.closest('button,a,input,select,textarea'))
            return;
        const q = bubble.getBoundingClientRect();
        dragState = {
            pointerId: event.pointerId,
            clientX: event.clientX,
            clientY: event.clientY,
            left: q.left,
            top: q.top
        };
        bubble.setPointerCapture(event.pointerId);
        bubble.style.cursor = 'grabbing';
        event.preventDefault();
    };

    bubble.addEventListener('pointerdown', onPointerDown);
    bubble.addEventListener('pointermove', onPointerMove);
    bubble.addEventListener('pointerup', finishDrag);
    bubble.addEventListener('pointercancel', finishDrag);

    const place = () => {
        if (manuallyPositioned) {
            keepManualPositionInViewport();
            return;
        }

        // Never expose a stale/clamped bubble while its target is being
        // replaced, laid out, or is still outside the viewport. Reconciliation
        // and scroll/resize observers will call place again when it is stable.
        bubble.style.visibility = 'hidden';
        if (!el.isConnected)
            return;

        const r = el.getBoundingClientRect();
        const targetInViewport =
            r.width > 0 &&
            r.height > 0 &&
            r.bottom > 0 &&
            r.right > 0 &&
            r.top < root.defaultView.innerHeight &&
            r.left < root.defaultView.innerWidth;
        if (!targetInViewport)
            return;

        const q = bubble.getBoundingClientRect();
        const gap = b.theme.pointerSize + 8;
        const viewportWidth = root.defaultView.innerWidth;
        const viewportHeight = root.defaultView.innerHeight;

        const coords = (candidate) => {
            if (candidate === 'Top')
                return [r.left + (r.width - q.width) / 2, r.top - q.height - gap];
            if (candidate === 'Left')
                return [r.left - q.width - gap, r.top + (r.height - q.height) / 2];
            if (candidate === 'Right')
                return [r.right + gap, r.top + (r.height - q.height) / 2];
            return [r.left + (r.width - q.width) / 2, r.bottom + gap];
        };

        const evaluate = (side) => {
            let [x, y] = coords(side);

            // Clamp only on the axis parallel to the target. Clamping across
            // the target-facing axis is what previously allowed the bubble to
            // slide back over the actionable element.
            if (side === 'Top' || side === 'Bottom')
                x = Math.max(margin, Math.min(x, viewportWidth - q.width - margin));
            else
                y = Math.max(margin, Math.min(y, viewportHeight - q.height - margin));

            const rect = {
                left: x, top: y,
                right: x + q.width, bottom: y + q.height
            };
            const inside =
                rect.left >= margin &&
                rect.top >= margin &&
                rect.right <= viewportWidth - margin &&
                rect.bottom <= viewportHeight - margin;
            const overlapsTarget = !(
                rect.right <= r.left ||
                rect.left >= r.right ||
                rect.bottom <= r.top ||
                rect.top >= r.bottom
            );
            const overflow =
                Math.max(0, margin - rect.left) +
                Math.max(0, margin - rect.top) +
                Math.max(0, rect.right - (viewportWidth - margin)) +
                Math.max(0, rect.bottom - (viewportHeight - margin));

            return { side, x, y, inside, overlapsTarget, overflow };
        };

        const preferred = b.placement === 'Auto' ? 'Bottom' : b.placement;
        const sides = [preferred, 'Top', 'Right', 'Left', 'Bottom']
            .filter((side, index, all) => all.indexOf(side) === index);
        const candidates = sides.map(evaluate);

        // First choice: fully visible and never covering the actionable target.
        // Fallback: still never cover the target; choose the least viewport
        // overflow. If no non-overlapping placement exists, keep the bubble
        // hidden rather than making the required control unusable.
        let chosen = candidates.find(candidate => candidate.inside && !candidate.overlapsTarget);
        if (!chosen) {
            const safe = candidates
                .filter(candidate => !candidate.overlapsTarget)
                .sort((a, b) => a.overflow - b.overflow);
            chosen = safe[0];
        }
        if (!chosen || !chosen.inside) {
            // A constrained child frame cannot paint outside its own viewport.
            // Mark the presentation for a top-level visual proxy instead of
            // exposing a clipped bubble inside the child document.
            bubble.dataset.actualPlacement = 'Overlay';
            bubble.style.pointerEvents = 'none';
            bubble.style.cursor = 'default';
            pointer.style.display = 'none';
            bubble.style.visibility = 'hidden';
            return;
        }

        bubble.style.pointerEvents = '';
        bubble.style.cursor = 'grab';
        bubble.style.left = chosen.x + 'px';
        bubble.style.top = chosen.y + 'px';
        bubble.dataset.actualPlacement = chosen.side;
        pointer.style.display = '';
        placePointer(chosen.side);
        bubble.style.visibility = 'visible';
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
        bubble.removeEventListener('pointerdown', onPointerDown);
        bubble.removeEventListener('pointermove', onPointerMove);
        bubble.removeEventListener('pointerup', finishDrag);
        bubble.removeEventListener('pointercancel', finishDrag);
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
                stepNumber,
                totalSteps,
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
        // If the target lives in a frame that is too small to contain the
        // bubble, render a visual proxy in the top-level document. The target
        // remains the validation owner in its original frame; this proxy is
        // presentation-only and uses page coordinates derived through the
        // iframe chain.
        var needsTopLevel = await resolution.Target.EvaluateAsync<bool>(
            @"el => {
                if (window === window.top) return false;
                const bubble=document.getElementById('dap-guide-bubble');
                if (!bubble || bubble.__dapTarget !== el || bubble.dataset.actualPlacement !== 'Overlay')
                    return false;
                return true;
            }");
        if (needsTopLevel)
        {
            var targetBox = await resolution.Target.BoundingBoxAsync();
            if (targetBox is not null)
            {
                var topLevelArgs = new
                {
                    content = step.Bubble.Content,
                    stepId = step.Id,
                    stepNumber,
                    totalSteps,
                    x = targetBox.X + targetBox.Width / 2,
                    y = targetBox.Y + targetBox.Height,
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
                };
                await page.MainFrame.EvaluateAsync(
                    @"b => {
                        document.getElementById('dap-guide-bubble-proxy')?.remove();
                        const bubble=document.createElement('div');
                        bubble.id='dap-guide-bubble-proxy';
                        bubble.dataset.dapStepId=b.stepId;
                        if(b.stepNumber && b.totalSteps) {
                            const progress=document.createElement('div');
                            progress.textContent='שלב '+b.stepNumber+' מתוך '+b.totalSteps;
                            Object.assign(progress.style,{fontSize:'12px',opacity:'0.78',marginBottom:'5px',fontWeight:'600'});
                            bubble.appendChild(progress);
                        }
                        const content=document.createElement('div');
                        content.textContent=b.content;
                        bubble.appendChild(content);
                        Object.assign(bubble.style,{
                            position:'fixed',zIndex:'2147483647',maxWidth:b.theme.maxWidth+'px',
                            padding:b.theme.padding,background:b.theme.backgroundColor,color:b.theme.textColor,
                            border:b.theme.borderWidth+'px solid '+b.theme.borderColor,
                            borderRadius:b.theme.borderRadius+'px',boxShadow:b.theme.boxShadow,
                            fontFamily:b.theme.fontFamily,fontSize:b.theme.fontSize+'px',
                            lineHeight:String(b.theme.lineHeight),direction:b.theme.direction,
                            pointerEvents:'none',visibility:'hidden'
                        });
                        document.body.appendChild(bubble);
                        const q=bubble.getBoundingClientRect(), margin=8, gap=8;
                        const left=Math.max(margin,Math.min(b.x-q.width/2,innerWidth-q.width-margin));
                        let top=b.y+gap;
                        if(top+q.height>innerHeight-margin)
                            top=Math.max(margin,b.y-q.height-gap);
                        bubble.style.left=left+'px';
                        bubble.style.top=Math.max(margin,Math.min(top,innerHeight-q.height-margin))+'px';
                        bubble.style.visibility='visible';
                    }",
                    topLevelArgs);
            }
        }

        var presentedAt = timing.Elapsed.TotalMilliseconds;
        if (!ensureOnly)
            Console.Error.WriteLine($"[DAP bubble] Step '{step.Id}' target resolved in {resolvedAt:F0} ms; DOM presentation completed in {presentedAt:F0} ms.");
        else if (presentedAt >= 250)
            Console.Error.WriteLine($"[DAP bubble] Step '{step.Id}' slow ensure presentation: resolve {resolvedAt:F0} ms; total {presentedAt:F0} ms.");

        return resolution;
    }

    public Task<TargetResolution<ILocator>> ResolveTargetAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken = default)
        => step.Target is null
            ? Task.FromResult(TargetResolution<ILocator>.NotFound())
            : _targets.ResolveAsync(page, step.Target, cancellationToken);

    public async Task HideAsync(IPage page)
    {
        const string script = """
() => {
    const proxy = document.getElementById('dap-guide-bubble-proxy');
    if (proxy) proxy.remove();
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
