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

  const theme={backgroundColor:"#312E5A",textColor:"#FFFFFF",borderColor:"#8B83C7",borderWidth:1,borderRadius:8,maxWidth:320,padding:"12px 16px",boxShadow:"0 10px 28px rgba(32,29,67,.28)",fontFamily:"Arial, sans-serif",fontSize:14,lineHeight:1.4,targetHighlightColor:"#A99FE8",targetHighlightWidth:2,targetHighlightShadow:"0 0 0 3px rgba(169,159,232,.22)",pointerSize:9}; let active=null;
  function hideBubble(){if(!active)return;active.cleanup?.();active=null;}
  function showBubble(step,stepNumber,totalSteps){
    hideBubble();const z=resolveTarget(step.target);if(z.status!=="resolved")return z;const el=z.element,root=el.ownerDocument;
    const ir=el.getBoundingClientRect();if(!(ir.width>0&&ir.height>0&&ir.bottom>0&&ir.right>0&&ir.top<innerHeight&&ir.left<innerWidth))el.scrollIntoView({behavior:"auto",block:"center",inline:"nearest"});
    const previous={outline:el.style.outline,outlineOffset:el.style.outlineOffset,boxShadow:el.style.boxShadow};
    el.style.outline=theme.targetHighlightWidth+"px solid "+theme.targetHighlightColor;el.style.outlineOffset="0px";el.style.boxShadow=theme.targetHighlightShadow;
    const b=root.createElement("div");b.id="dap-guide-bubble";b.dataset.dapStepId=step.id;b.dataset.placement=String(step.bubble?.placement||"Auto");b.__dapTarget=el;b.setAttribute("role","status");
    const handle=root.createElement("div");handle.dataset.dapDragHandle="1";handle.textContent="⠿";handle.title="גרור בועית";Object.assign(handle.style,{display:"block",width:"fit-content",marginLeft:"auto",marginRight:"auto",textAlign:"center",fontSize:"18px",lineHeight:"14px",opacity:".72",marginBottom:"6px",cursor:"grab",touchAction:"none"});
    const content=root.createElement("div");content.textContent=step.bubble?.content||"";content.style.cursor="default";b.append(handle,content);
    if(stepNumber&&totalSteps){const p=root.createElement("div");p.textContent="שלב "+stepNumber+" מתוך "+totalSteps;Object.assign(p.style,{fontSize:"12px",opacity:".78",marginTop:"8px",fontWeight:"600",cursor:"default"});b.appendChild(p);}
    const pointer=root.createElement("div");pointer.dataset.dapPointer="1";Object.assign(pointer.style,{position:"absolute",width:"0",height:"0",cursor:"default"});b.appendChild(pointer);
    Object.assign(b.style,{position:"fixed",zIndex:"2147483646",maxWidth:theme.maxWidth+"px",padding:theme.padding,background:theme.backgroundColor,color:theme.textColor,border:theme.borderWidth+"px solid "+theme.borderColor,borderRadius:theme.borderRadius+"px",boxShadow:theme.boxShadow,fontFamily:theme.fontFamily,fontSize:theme.fontSize+"px",lineHeight:String(theme.lineHeight),direction:"rtl",visibility:"hidden",touchAction:"none",userSelect:"none"});b.style.setProperty("cursor","default","important");root.body.appendChild(b);
    let manual=false,drag=null;const margin=8;
    const clamp=(x,y)=>{const q=b.getBoundingClientRect();return{x:Math.max(margin,Math.min(x,innerWidth-q.width-margin)),y:Math.max(margin,Math.min(y,innerHeight-q.height-margin))}};
    const pointerFor=side=>{const n=theme.pointerSize;pointer.style.cssText="position:absolute;width:0;height:0;cursor:default;border-left:"+n+"px solid transparent;border-right:"+n+"px solid transparent;border-top:"+n+"px solid transparent;border-bottom:"+n+"px solid transparent";if(side==="Top"){pointer.style.left="50%";pointer.style.bottom=(-2*n)+"px";pointer.style.transform="translateX(-50%)";pointer.style.borderTopColor=theme.backgroundColor}else if(side==="Bottom"){pointer.style.left="50%";pointer.style.top=(-2*n)+"px";pointer.style.transform="translateX(-50%)";pointer.style.borderBottomColor=theme.backgroundColor}else if(side==="Left"){pointer.style.top="50%";pointer.style.right=(-2*n)+"px";pointer.style.transform="translateY(-50%)";pointer.style.borderLeftColor=theme.backgroundColor}else{pointer.style.top="50%";pointer.style.left=(-2*n)+"px";pointer.style.transform="translateY(-50%)";pointer.style.borderRightColor=theme.backgroundColor}};
    const place=()=>{if(manual){const q=b.getBoundingClientRect(),n=clamp(q.left,q.top);b.style.left=n.x+"px";b.style.top=n.y+"px";b.style.visibility="visible";return}if(!el.isConnected){b.style.visibility="hidden";return}const r=el.getBoundingClientRect(),q=b.getBoundingClientRect(),gap=theme.pointerSize+8;if(!(r.width>0&&r.height>0&&r.bottom>0&&r.right>0&&r.top<innerHeight&&r.left<innerWidth)){b.style.visibility="hidden";return}const coords=x=>x==="Top"?[r.left+(r.width-q.width)/2,r.top-q.height-gap]:x==="Left"?[r.left-q.width-gap,r.top+(r.height-q.height)/2]:x==="Right"?[r.right+gap,r.top+(r.height-q.height)/2]:[r.left+(r.width-q.width)/2,r.bottom+gap];const preferred=String(step.bubble?.placement||"Auto");const sides=[preferred==="Auto"?"Bottom":preferred,"Top","Right","Left","Bottom"].filter((x,i,a)=>a.indexOf(x)===i);let chosen=null;for(const side of sides){let [x,y]=coords(side);if(side==="Top"||side==="Bottom")x=Math.max(margin,Math.min(x,innerWidth-q.width-margin));else y=Math.max(margin,Math.min(y,innerHeight-q.height-margin));const inside=x>=margin&&y>=margin&&x+q.width<=innerWidth-margin&&y+q.height<=innerHeight-margin;const overlap=!(x+q.width<=r.left||x>=r.right||y+q.height<=r.top||y>=r.bottom);if(inside&&!overlap){chosen={side,x,y};break}}if(!chosen){b.style.visibility="hidden";return}b.style.left=chosen.x+"px";b.style.top=chosen.y+"px";pointer.style.display="";pointerFor(chosen.side);b.style.visibility="visible";b.dataset.actualPlacement=chosen.side};
    const down=e=>{if(e.button!==0||!e.target.closest('[data-dap-drag-handle="1"]'))return;const q=b.getBoundingClientRect();drag={id:e.pointerId,x:e.clientX,y:e.clientY,left:q.left,top:q.top};b.setPointerCapture(e.pointerId);handle.style.setProperty("cursor","grabbing","important");e.preventDefault()};const move=e=>{if(!drag||e.pointerId!==drag.id)return;const n=clamp(drag.left+e.clientX-drag.x,drag.top+e.clientY-drag.y);b.style.left=n.x+"px";b.style.top=n.y+"px"};const up=e=>{if(!drag||e.pointerId!==drag.id)return;manual=true;drag=null;handle.style.setProperty("cursor","grab","important");pointer.style.display="none";b.dataset.manualPosition="true";try{b.releasePointerCapture(e.pointerId)}catch{}};
    b.addEventListener("pointerdown",down);b.addEventListener("pointermove",move);b.addEventListener("pointerup",up);b.addEventListener("pointercancel",up);const ro=new ResizeObserver(place);ro.observe(el);ro.observe(b);addEventListener("scroll",place,true);addEventListener("resize",place);place();
    active={bubble:b,step,element:el,cleanup:()=>{ro.disconnect();removeEventListener("scroll",place,true);removeEventListener("resize",place);b.remove();el.style.outline=previous.outline;el.style.outlineOffset=previous.outlineOffset;el.style.boxShadow=previous.boxShadow}};
    return{status:"resolved",count:1,rect:z.rect};
  }
  function reconcile(){if(!active)return;const z=resolveTarget(active.step.target);if(z.status!=="resolved"){hideBubble();return}if(z.element!==active.element){const step=active.step;const g=globalThis.__dapWebRuntime?.guide;showBubble(step,g?g.index+1:null,g?.steps.length);}}
  function inputValue(el){return "value" in el ? String(el.value ?? "") : "";}
  function validationSatisfied(el,v){if(!v)return false;const kind=String(v.kind??v.Kind??"").toLowerCase(),expected=v.expectedValue??v.ExpectedValue;if(kind==="clicked")return true;if(kind==="value-not-empty")return inputValue(el).trim().length>0;if(kind==="value-equals")return expected!=null&&inputValue(el)===String(expected);throw new Error("Unsupported Web validation kind '"+kind+"'.");}
  function emitStepEvent(type,step,extra={}){document.dispatchEvent(new CustomEvent("dap:web-runtime-step-event",{detail:{type,stepId:step.id,...extra}}));}
  function advanceGuide(step){const g=globalThis.__dapWebRuntime?.guide;if(!g||g.steps[g.index]?.id!==step.id)return;const mode=String(step.advanceMode??"automaticOnValidation").replace(/[^a-z]/gi,"").toLowerCase();if(mode!=="automaticonvalidation")return;g.index++;if(g.index>=g.steps.length){hideBubble();emitStepEvent("guide-completed",step,{guideId:g.guideId});return;}const next=g.steps[g.index];showBubble(next,g.index+1,g.steps.length);emitStepEvent("step-presented",next,{guideId:g.guideId,stepNumber:g.index+1,totalSteps:g.steps.length});}
  document.addEventListener("click",e=>{if(active&&validationKind(active.step)==="clicked"&&(active.element===e.target||active.element.contains(e.target))){const s=active.step;emitStepEvent("validation-satisfied",s,{kind:"clicked"});queueMicrotask(()=>advanceGuide(s));}},true);
  const edited=new WeakSet();
  function validationKind(step){return String(step?.validation?.kind??step?.validation?.Kind??"").toLowerCase();}
  document.addEventListener("input",e=>{if(active&&active.element===e.target)edited.add(e.target)},true);
  document.addEventListener("change",e=>{if(!active||active.element!==e.target||!active.step.validation||validationKind(active.step)==="clicked")return;const tag=e.target.tagName?.toLowerCase(),type=(e.target.getAttribute?.("type")||"").toLowerCase(),text=tag==="textarea"||(tag==="input"&&!["checkbox","radio","button","submit","reset"].includes(type));if(text){edited.add(e.target);return}if(validationSatisfied(e.target,active.step.validation)){const s=active.step;emitStepEvent("validation-satisfied",s,{kind:validationKind(s)});queueMicrotask(()=>advanceGuide(s));}},true);
  document.addEventListener("blur",e=>{if(!active||active.element!==e.target||!active.step.validation||validationKind(active.step)==="clicked"||!edited.has(e.target))return;edited.delete(e.target);if(validationSatisfied(e.target,active.step.validation)){const s=active.step;emitStepEvent("validation-satisfied",s,{kind:validationKind(s)});queueMicrotask(()=>advanceGuide(s));}},true);
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
