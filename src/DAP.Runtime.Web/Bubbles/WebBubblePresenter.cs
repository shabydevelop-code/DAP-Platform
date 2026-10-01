using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Bubbles;

public sealed class WebBubblePresenter
{
    private readonly WebTargetResolver _targets;
    public WebBubblePresenter(WebTargetResolver targets) => _targets = targets;

    public async Task<TargetResolution<ILocator>> ShowAsync(IPage page, GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Target is null) return TargetResolution<ILocator>.NotFound();
        var resolution = await _targets.ResolveAsync(page, step.Target, cancellationToken);
        if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null) return resolution;

        await resolution.Target.EvaluateAsync("""(el, b) => {
          const root = el.ownerDocument;
          root.getElementById('dap-guide-bubble')?.remove();
          const bubble = root.createElement('div');
          bubble.id='dap-guide-bubble'; bubble.setAttribute('role','status'); bubble.dataset.placement=b.placement;
          bubble.textContent=b.content;
          Object.assign(bubble.style,{position:'fixed',zIndex:'2147483646',maxWidth:'320px',padding:'12px 16px',background:'#fff',color:'#1f2937',border:'1px solid #cbd5e1',borderRadius:'10px',boxShadow:'0 8px 24px rgba(0,0,0,.18)',fontFamily:'Arial,sans-serif',fontSize:'14px',lineHeight:'1.4',direction:'rtl'});
          root.body.appendChild(bubble);
          const place=()=>{const r=el.getBoundingClientRect(), q=bubble.getBoundingClientRect(), gap=10;let x=r.right+gap,y=r.top+(r.height-q.height)/2;if(b.placement==='Top'){x=r.left+(r.width-q.width)/2;y=r.top-q.height-gap}else if(b.placement==='Bottom'||b.placement==='Auto'){x=r.left+(r.width-q.width)/2;y=r.bottom+gap}else if(b.placement==='Left'){x=r.left-q.width-gap;y=r.top+(r.height-q.height)/2}x=Math.max(8,Math.min(x,innerWidth-q.width-8));y=Math.max(8,Math.min(y,innerHeight-q.height-8));bubble.style.left=x+'px';bubble.style.top=y+'px'};
          place();
          const ro=new ResizeObserver(place); ro.observe(el); ro.observe(bubble);
          root.defaultView.addEventListener('scroll',place,true); root.defaultView.addEventListener('resize',place);
          bubble.__dapCleanup=()=>{ro.disconnect();root.defaultView.removeEventListener('scroll',place,true);root.defaultView.removeEventListener('resize',place)};
        }""", new { content = step.Bubble.Content, placement = step.Bubble.Placement.ToString() });
        return resolution;
    }

    public async Task HideAsync(IPage page)
    {
        foreach (var frame in page.Frames)
            try { await frame.EvaluateAsync("() => { const b=document.getElementById('dap-guide-bubble'); if(b){b.__dapCleanup?.();b.remove()} }"); } catch (PlaywrightException) { }
    }
}
