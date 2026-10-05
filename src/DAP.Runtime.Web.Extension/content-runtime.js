(() => {
  "use strict";

  if (globalThis.__dapWebRuntime) return;

  const normalize = value => String(value ?? "").replace(/\s+/g, " ").trim();

  function byText(value) {
    const wanted = normalize(value);
    return [...document.querySelectorAll("body *")]
      .filter(el => normalize(el.textContent) === wanted);
  }

  function byLabel(value) {
    const wanted = normalize(value);
    const result = [];
    for (const label of document.querySelectorAll("label")) {
      if (normalize(label.textContent) !== wanted) continue;
      if (label.htmlFor) {
        const target = document.getElementById(label.htmlFor);
        if (target) result.push(target);
      } else {
        const target = label.querySelector("input,select,textarea,button");
        if (target) result.push(target);
      }
    }
    return result;
  }

  function implicitRole(el) {
    const tag = el.tagName.toLowerCase();
    if (tag === "button") return "button";
    if (tag === "a" && el.hasAttribute("href")) return "link";
    if (tag === "select") return "combobox";
    if (tag === "textarea") return "textbox";
    if (tag === "input") {
      const type = (el.getAttribute("type") || "text").toLowerCase();
      if (["button","submit","reset"].includes(type)) return "button";
      if (type === "checkbox") return "checkbox";
      if (type === "radio") return "radio";
      return "textbox";
    }
    return null;
  }

  function createCandidates(locator) {
    const strategy = locator.strategy.trim().toLowerCase();
    if (strategy === "css") return [...document.querySelectorAll(locator.value)];
    if (strategy === "text") return byText(locator.value);
    if (strategy === "label") return byLabel(locator.value);
    if (strategy === "role") {
      const role = locator.value.trim().toLowerCase();
      return [...document.querySelectorAll("body *")]
        .filter(el => (el.getAttribute("role") || implicitRole(el)) === role);
    }
    throw new Error("Unsupported Web locator strategy '" + locator.strategy + "'.");
  }

  function matchesAnchor(el, anchor) {
    if (anchor.locator.strategy.toLowerCase() !== "css")
      throw new Error("Initial Web anchor relations support CSS anchors only.");

    const selector = anchor.locator.value;
    switch (String(anchor.relation).toLowerCase()) {
      case "ancestor":
      case "context": return !!el.closest(selector);
      case "descendant": return !!el.querySelector(selector);
      case "sibling":
        return !!(el.parentElement &&
          [...el.parentElement.children].some(x => x !== el && x.matches(selector)));
      case "nearby":
        return !!(el.parentElement && el.parentElement.querySelector(selector));
      default: return false;
    }
  }

  function resolveTarget(descriptor) {
    const matches = createCandidates(descriptor.locator)
      .filter(el => (descriptor.anchors || []).every(a => matchesAnchor(el, a)));

    if (matches.length === 0) return { status: "notFound", count: 0 };
    if (matches.length > 1) return { status: "ambiguous", count: matches.length };

    const el = matches[0];
    const rect = el.getBoundingClientRect();
    return {
      status: "resolved",
      count: 1,
      element: el,
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
    };
  }

  const theme={background:"#312E5A",text:"#FFFFFF",border:"#8B83C7",highlight:"#A99FE8"}; let active=null;
  function hideBubble(){if(!active)return;active.bubble.remove();active.highlight.remove();active=null;}
  function place(b,r){const g=12,q=b.getBoundingClientRect(),v=[[r.left+r.width/2-q.width/2,r.bottom+g],[r.left+r.width/2-q.width/2,r.top-q.height-g],[r.right+g,r.top+r.height/2-q.height/2],[r.left-q.width-g,r.top+r.height/2-q.height/2]];let x=v[0];for(const z of v)if(z[0]>=8&&z[1]>=8&&z[0]+q.width<=innerWidth-8&&z[1]+q.height<=innerHeight-8){x=z;break}b.style.left=Math.max(8,Math.min(innerWidth-q.width-8,x[0]))+"px";b.style.top=Math.max(8,Math.min(innerHeight-q.height-8,x[1]))+"px";}
  function showBubble(step,stepNumber,totalSteps){hideBubble();const z=resolveTarget(step.target);if(z.status!=="resolved")return z;z.element.scrollIntoView({block:"center",inline:"nearest"});const r=z.element.getBoundingClientRect(),h=document.createElement("div"),b=document.createElement("div");Object.assign(h.style,{position:"fixed",pointerEvents:"none",zIndex:"2147483645",left:(r.left-2)+"px",top:(r.top-2)+"px",width:(r.width+4)+"px",height:(r.height+4)+"px",border:"2px solid "+theme.highlight,borderRadius:"4px",boxSizing:"border-box",boxShadow:"0 0 0 3px rgba(169,159,232,.22)"});Object.assign(b.style,{position:"fixed",zIndex:"2147483646",maxWidth:"320px",padding:"12px 16px",borderRadius:"8px",border:"1px solid "+theme.border,background:theme.background,color:theme.text,boxShadow:"0 10px 28px rgba(32,29,67,.28)",fontFamily:"Arial,sans-serif",fontSize:"14px",lineHeight:"1.4",direction:"rtl",boxSizing:"border-box"});b.textContent=step.bubble?.content||"";if(stepNumber&&totalSteps){const s=document.createElement("div");s.textContent=stepNumber+" / "+totalSteps;s.style.cssText="margin-top:8px;font-size:11px;opacity:.75;text-align:left;direction:ltr";b.appendChild(s)}document.documentElement.append(h,b);place(b,r);active={bubble:b,highlight:h,step};return{status:"resolved",count:1,rect:z.rect};}
  function reconcile(){if(!active)return;const z=resolveTarget(active.step.target);if(z.status!=="resolved"){hideBubble();return}const r=z.element.getBoundingClientRect();Object.assign(active.highlight.style,{left:(r.left-2)+"px",top:(r.top-2)+"px",width:(r.width+4)+"px",height:(r.height+4)+"px"});place(active.bubble,r);}
  const listeners = new Set();
  const observer = new MutationObserver(records => {
    for (const listener of listeners) listener(records);\n    queueMicrotask(reconcile);
  });

  const start = () => {
    const root = document.documentElement;
    if (!root) return requestAnimationFrame(start);
    observer.observe(root, {
      subtree: true, childList: true, attributes: true, characterData: true
    });
  };

  start();

  globalThis.__dapWebRuntime = {
    version: "0.1.0",
    resolveTarget,\n    showBubble,\n    hideBubble,
    onMutation(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  };

  addEventListener("resize",reconcile,{passive:true}); addEventListener("scroll",reconcile,{passive:true,capture:true});
  document.addEventListener("dap:web-runtime-command",e=>{const q=e.detail||{};try{let result;if(q.type==="showStep")result=showBubble(q.step,q.stepNumber,q.totalSteps);else if(q.type==="hide"){hideBubble();result={status:"hidden"};}else if(q.type==="resolve")result=resolveTarget(q.target);else throw new Error("Unknown DAP command");document.dispatchEvent(new CustomEvent("dap:web-runtime-result",{detail:{requestId:q.requestId,ok:true,result}}));}catch(error){document.dispatchEvent(new CustomEvent("dap:web-runtime-result",{detail:{requestId:q.requestId,ok:false,error:String(error?.message||error)}}));}});
  document.dispatchEvent(new CustomEvent("dap:web-runtime-ready", {
    detail: { version: globalThis.__dapWebRuntime.version }
  }));
})();
