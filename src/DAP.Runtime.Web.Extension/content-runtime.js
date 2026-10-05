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
  function showBubble(step,stepNumber,totalSteps){hideBubble();const z=resolveTarget(step.target);if(z.status!=="resolved")return z;z.element.scrollIntoView({block:"center",inline:"nearest"});const r=z.element.getBoundingClientRect(),h=document.createElement("div"),b=document.createElement("div");Object.assign(h.style,{position:"fixed",pointerEvents:"none",zIndex:"2147483645",left:(r.left-2)+"px",top:(r.top-2)+"px",width:(r.width+4)+"px",height:(r.height+4)+"px",border:"2px solid "+theme.highlight,borderRadius:"4px",boxSizing:"border-box",boxShadow:"0 0 0 3px rgba(169,159,232,.22)"});Object.assign(b.style,{position:"fixed",zIndex:"2147483646",maxWidth:"320px",padding:"12px 16px",borderRadius:"8px",border:"1px solid "+theme.border,background:theme.background,color:theme.text,boxShadow:"0 10px 28px rgba(32,29,67,.28)",fontFamily:"Arial,sans-serif",fontSize:"14px",lineHeight:"1.4",direction:"rtl",boxSizing:"border-box"});b.textContent=step.bubble?.content||"";if(stepNumber&&totalSteps){const s=document.createElement("div");s.textContent=stepNumber+" / "+totalSteps;s.style.cssText="margin-top:8px;font-size:11px;opacity:.75;text-align:left;direction:ltr";b.appendChild(s)}document.documentElement.append(h,b);place(b,r);active={bubble:b,highlight:h,step,element:z.element};return{status:"resolved",count:1,rect:z.rect};}
  function reconcile(){if(!active)return;const z=resolveTarget(active.step.target);if(z.status!=="resolved"){hideBubble();return}const r=z.element.getBoundingClientRect();Object.assign(active.highlight.style,{left:(r.left-2)+"px",top:(r.top-2)+"px",width:(r.width+4)+"px",height:(r.height+4)+"px"});place(active.bubble,r);active.element=z.element;}
  function inputValue(el){return "value" in el ? String(el.value ?? "") : "";}
  function validationSatisfied(el,v){if(!v)return false;if(v.kind==="clicked")return true;if(v.kind==="value-not-empty")return inputValue(el).trim().length>0;if(v.kind==="value-equals")return v.expectedValue!=null&&inputValue(el)===String(v.expectedValue);throw new Error("Unsupported Web validation kind '"+v.kind+"'.");}
  function emitStepEvent(type,step,extra={}){document.dispatchEvent(new CustomEvent("dap:web-runtime-step-event",{detail:{type,stepId:step.id,...extra}}));}
  function advanceGuide(step){const g=globalThis.__dapWebRuntime?.guide;if(!g||g.steps[g.index]?.id!==step.id)return;if(String(step.advanceMode).toLowerCase()!=="automaticonvalidation")return;g.index++;if(g.index>=g.steps.length){hideBubble();emitStepEvent("guide-completed",step,{guideId:g.guideId});return;}const next=g.steps[g.index];showBubble(next,g.index+1,g.steps.length);emitStepEvent("step-presented",next,{guideId:g.guideId,stepNumber:g.index+1,totalSteps:g.steps.length});}
  document.addEventListener("click",e=>{if(active&&active.step.validation?.kind==="clicked"&&active.element===e.target){const s=active.step;emitStepEvent("validation-satisfied",s,{kind:"clicked"});queueMicrotask(()=>advanceGuide(s));}},true);
  document.addEventListener("change",e=>{if(active&&active.element===e.target&&active.step.validation&&active.step.validation.kind!=="clicked"&&validationSatisfied(e.target,active.step.validation)){const s=active.step;emitStepEvent("validation-satisfied",s,{kind:s.validation.kind});queueMicrotask(()=>advanceGuide(s));}},true);
  document.addEventListener("blur",e=>{if(active&&active.element===e.target&&active.step.validation&&active.step.validation.kind!=="clicked"&&validationSatisfied(e.target,active.step.validation)){emitStepEvent("validation-satisfied",active.step,{kind:active.step.validation.kind});}},true);
  const listeners = new Set();
  const observer = new MutationObserver(records => {
    for (const listener of listeners) listener(records);
    queueMicrotask(reconcile);
  });

  const start = () => {
    const root = document.documentElement;
    if (!root) return requestAnimationFrame(start);
    observer.observe(root, {
      subtree: true, childList: true, attributes: true, characterData: true
    });
  };

  start();

  async function loadGuide(guideId) {
    const requestId = crypto.randomUUID();
    const response = await chrome.runtime.sendMessage({
      type: "dap-native", requestId, payload: { type: "getGuide", guideId }
    });
    if (!response?.ok) throw new Error(response?.error || "Failed to load DAP guide.");
    return response.steps || [];
  }

  async function startGuide(guideId) {
    const steps = await loadGuide(guideId);
    if (!steps.length) throw new Error("Guide '"+guideId+"' has no steps.");
    const state = { guideId, steps, index: 0 };
    globalThis.__dapWebRuntime.guide = state;
    const result = showBubble(steps[0], 1, steps.length);
    return { guideId, stepCount: steps.length, stepId: steps[0].id, result };
  }

  globalThis.__dapWebRuntime = {
    version: "0.1.0",
    resolveTarget,
    showBubble,
    hideBubble,
    loadGuide,
    startGuide,
    guide: null,
    onMutation(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  };

  addEventListener("resize",reconcile,{passive:true}); addEventListener("scroll",reconcile,{passive:true,capture:true});
  document.addEventListener("dap:web-runtime-command",e=>{const q=e.detail||{};try{let result;if(q.type==="showStep")result=showBubble(q.step,q.stepNumber,q.totalSteps);else if(q.type==="hide"){hideBubble();result={status:"hidden"};}else if(q.type==="resolve")result=resolveTarget(q.target);else throw new Error("Unknown DAP command");document.dispatchEvent(new CustomEvent("dap:web-runtime-result",{detail:{requestId:q.requestId,ok:true,result}}));}catch(error){document.dispatchEvent(new CustomEvent("dap:web-runtime-result",{detail:{requestId:q.requestId,ok:false,error:String(error?.message||error)}}));}});
  chrome.runtime.onMessage.addListener((message,sender,sendResponse)=>{if(message?.type!=="dap-start-guide")return;(async()=>{try{const result=await startGuide(message.guideId);sendResponse({ok:true,...result});}catch(error){sendResponse({ok:false,error:String(error?.message||error)});}})();return true;});
  document.dispatchEvent(new CustomEvent("dap:web-runtime-ready", {
    detail: { version: globalThis.__dapWebRuntime.version }
  }));
})();
