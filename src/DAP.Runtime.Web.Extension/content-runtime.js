(() => {
  "use strict";

  // Do not return before registering the adapter endpoint. After an extension
  // reload a tab may still contain an older __dapWebRuntime object while the
  // new extension context needs to install its current message listener.
  if (globalThis.__dapAdapterEndpointVersion === "0.3.0") return;
  globalThis.__dapAdapterEndpointVersion = "0.3.0";

  // Register the adapter message endpoint before the legacy POC runtime is
  // initialized. Target resolution is looked up at message time, so an
  // initialization failure later in this script can no longer make the frame
  // appear to have no DAP content script at all.
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type !== "dap-adapter-command") return;
    try {
      const command = message.command || {};
      if (command.type === "ping") {
        sendResponse({ok:true,result:{ready:true,version:"0.3.0"}});
        return;
      }
      if (command.type === "resolveFrameChild") {
        const matches = createCandidates(command.locator);
        if (matches.length !== 1) {
          sendResponse({ok:true,result:{status:matches.length ? "ambiguous" : "notFound",count:matches.length}});
          return;
        }
        const frame = matches[0];
        if (!(frame instanceof HTMLIFrameElement || frame instanceof HTMLFrameElement) || !frame.contentWindow) {
          sendResponse({ok:true,result:{status:"notFound",count:0}});
          return;
        }
        const token = crypto.randomUUID();
        const handler = event => {
          if (event.source !== frame.contentWindow || event.data?.type !== "dap-frame-identify-response" || event.data?.token !== token) return;
          removeEventListener("message", handler);
          const rect=frame.getBoundingClientRect();
          sendResponse({ok:true,result:{
            status:"resolved",
            count:1,
            frameToken:token,
            rect:{x:rect.x,y:rect.y,width:rect.width,height:rect.height}
          }});
        };
        addEventListener("message", handler);
        frame.contentWindow.postMessage({type:"dap-frame-identify",token}, "*");
        setTimeout(() => { removeEventListener("message", handler); sendResponse({ok:true,result:{status:"notFound",count:0}}); }, 1000);
        return true;
      }
      if (command.type === "resolveTarget") {
        const result = resolveTarget(command.target);
        sendResponse({ok:true,result:{status:result.status,count:result.count}});
        return;
      }
      if (command.type === "isContextActive") {
        const context = command.context;
        if (!context) {
          sendResponse({ok:true,result:{active:true}});
          return;
        }
        let active;
        if (context.kind === "url-equals") active = location.href === context.value;
        else if (context.kind === "url-contains") active = location.href.includes(context.value);
        else if (context.kind === "url-fragment-equals") active = location.hash === context.value;
        else if (context.kind === "css-exists") active = createCandidates({strategy:"css",value:context.value}).length > 0;
        else throw new Error("Unsupported Web Step context kind '" + context.kind + "'.");
        sendResponse({ok:true,result:{active}});
        return;
      }
      if (command.type === "inspectTarget") {
        const result = resolveTarget(command.target);
        if (result.status !== "resolved") {
          sendResponse({ok:true,result:{status:result.status,count:result.count,value:null,enabled:false,text:null}});
          return;
        }
        const el = result.element;
        sendResponse({ok:true,result:{
          status:"resolved",count:1,
          value:"value" in el ? String(el.value ?? "") : "",
          enabled:!(el.disabled === true || el.getAttribute("aria-disabled") === "true"),
          text:el.textContent ?? ""
        }});
        return;
      }
      if (command.type === "capture") {
        const capture = command.capture;
        let raw = null;
        if (capture.property === "frame-url") raw = location.href;
        else if (capture.property === "frame-url-fragment") raw = location.hash;
        else {
          const matches = createCandidates(capture.locator);
          if (matches.length !== 1) {
            sendResponse({ok:true,result:{status:matches.length ? "ambiguous" : "notFound",count:matches.length,value:null}});
            return;
          }
          raw = capture.property === "text" ? matches[0].textContent :
                capture.property === "value" && "value" in matches[0] ? String(matches[0].value ?? "") : null;
        }
        sendResponse({ok:true,result:{status:raw == null ? "notFound" : "resolved",count:raw == null ? 0 : 1,value:raw}});
        return;
      }
      if (command.type === "ensureBubble") {
        const result = resolveTarget(command.step?.target);
        if (result.status !== "resolved") {
          hideBubble();
          sendResponse({ok:true,result:{status:result.status,count:result.count}});
          return;
        }
        if (bubbleState?.bubble && bubbleState.step?.id === command.step?.id && bubbleState.element === result.element) {
          sendResponse({ok:true,result:{status:"resolved",count:1}});
          return;
        }
        const shown = showBubble(command.step, command.stepNumber, command.totalSteps);
        sendResponse({ok:true,result:shown});
        return;
      }
      if (command.type === "showBubbleProxy") {
        const result=showBubbleProxy(command.step,command.stepNumber,command.totalSteps,command.targetRect);
        sendResponse({ok:true,result});
        return;
      }
      if (command.type === "showCenteredStep") {
        showCenteredMessage({
          elementId:"dap-guide-centered",
          content:command.step?.bubble?.content||"",
          actionText:command.actionText||"אישור",
          actionDataKey:"dapGuideConfirm",
          progressText:command.progressText||null,
          dragText:command.dragText||"גרור להזזת הבועה",
          eventType:"centered-dismissed",
          stepId:command.step?.id||null
        });
        sendResponse({ok:true,result:{status:"shown"}});
        return;
      }
      if (command.type === "showGuideCompleted") {
        showCenteredMessage({
          elementId:"dap-guide-completed",
          content:command.content||"",
          actionText:command.actionText||"סיום",
          actionDataKey:"dapGuideFinish",
          progressText:null,
          dragText:command.dragText||"גרור להזזת הבועה",
          eventType:"guide-completed-dismissed",
          stepId:null
        });
        sendResponse({ok:true,result:{status:"shown"}});
        return;
      }
      if (command.type === "hideBubble") {
        hideBubble();
        sendResponse({ok:true,result:{status:"hidden"}});
        return;
      }
      if (command.type === "armValidation") {
        const result = resolveTarget(command.step?.target);
        if (result.status !== "resolved") {
          sendResponse({ok:true,result:{status:result.status,count:result.count}});
          return;
        }
        armValidationTarget(command.step, result.element, command.armId);
        sendResponse({ok:true,result:{status:"resolved",count:1}});
        return;
      }
      if (command.type === "readTargetValue") {
        const result = resolveTarget(command.target);
        if (result.status !== "resolved") {
          sendResponse({ok:true,result:{status:result.status,count:result.count,value:null}});
          return;
        }
        const value = "value" in result.element ? String(result.element.value ?? "") : "";
        sendResponse({ok:true,result:{status:"resolved",count:1,value}});
        return;
      }
      if (command.type === "waitForDomQuiet") {
        const quietMs = Math.max(0, Number(command.quietMilliseconds || 0));
        let timer;
        let settled = false;
        const finish = () => {
          if (settled) return;
          settled = true;
          observer.disconnect();
          sendResponse({ok:true,result:{stable:true}});
        };
        const arm = () => {
          clearTimeout(timer);
          timer = setTimeout(finish, quietMs);
        };
        const observer = new MutationObserver(arm);
        const root = document.documentElement;
        if (!root) {
          sendResponse({ok:true,result:{stable:false}});
          return;
        }
        observer.observe(root, {subtree:true,childList:true,attributes:true,characterData:true});
        arm();
        return true;
      }
      sendResponse({ok:false,error:"Unsupported DAP adapter command '"+String(command.type||"")+"'."});
    } catch (error) {
      sendResponse({ok:false,error:String(error?.message||error)});
    }
  });

  addEventListener("message", event => {
    if (event.data?.type !== "dap-frame-identify" || !event.data?.token) return;
    event.source?.postMessage({type:"dap-frame-identify-response",token:event.data.token}, {targetOrigin:"*"});
    chrome.runtime.sendMessage({type:"dap-frame-identified",token:event.data.token}).catch(()=>{});
  });

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

  function queryPlaywrightCss(selector, root = document) {
    const source = String(selector || "");
    const textFilters = [];
    const nativeSelector = source.replace(
      /:has-text\((["'])(.*?)\1\)/g,
      (_match, _quote, text) => {
        textFilters.push(normalize(text).toLowerCase());
        return "";
      });

    const candidates = [...root.querySelectorAll(nativeSelector || "*")];
    if (!textFilters.length) return candidates;

    return candidates.filter(el => {
      const text = normalize(el.textContent).toLowerCase();
      return textFilters.every(wanted => text.includes(wanted));
    });
  }

  function matchesPlaywrightCss(el, selector) {
    const source = String(selector || "");
    const textFilters = [];
    const nativeSelector = source.replace(
      /:has-text\((["'])(.*?)\1\)/g,
      (_match, _quote, text) => {
        textFilters.push(normalize(text).toLowerCase());
        return "";
      });

    if (nativeSelector && !el.matches(nativeSelector)) return false;
    if (!textFilters.length) return true;

    const text = normalize(el.textContent).toLowerCase();
    return textFilters.every(wanted => text.includes(wanted));
  }

  function createCandidates(locator) {
    const strategy = locator.strategy.trim().toLowerCase();
    if (strategy === "css") return queryPlaywrightCss(locator.value);
    if (strategy === "text") {
      const wanted = normalize(locator.value).toLowerCase();
      return [...document.querySelectorAll("body *")]
        .filter(el => normalize(el.textContent).toLowerCase().includes(wanted));
    }
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
      case "context": {
        for (let current = el; current; current = current.parentElement) {
          if (matchesPlaywrightCss(current, selector)) return true;
        }
        return false;
      }
      case "descendant":
        return queryPlaywrightCss(selector, el).length > 0;
      case "sibling":
        return !!(el.parentElement &&
          [...el.parentElement.children].some(x => x !== el && matchesPlaywrightCss(x, selector)));
      case "nearby":
        return !!(el.parentElement && queryPlaywrightCss(selector, el.parentElement).length > 0);
      default:
        return false;
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

  const theme={backgroundColor:"#312E5A",textColor:"#FFFFFF",borderColor:"#8B83C7",borderWidth:1,borderRadius:8,maxWidth:320,padding:"12px 16px",boxShadow:"0 10px 28px rgba(32,29,67,.28)",fontFamily:"Arial, sans-serif",fontSize:14,lineHeight:1.4,targetHighlightColor:"#A99FE8",targetHighlightWidth:2,targetHighlightShadow:"0 0 0 3px rgba(169,159,232,.22)",pointerSize:9};
  let bubbleState=null;
  let validationState=null;
  function hideBubble(){
    // A page can survive an extension reload while its old isolated-world
    // JavaScript state is discarded. Remove any DOM presentation by id as
    // well as the presentation owned by this content-script instance.
    const stale=document.getElementById("dap-guide-bubble");
    document.getElementById("dap-guide-bubble-proxy")?.remove();
    document.getElementById("dap-guide-centered")?.remove();
    document.getElementById("dap-guide-completed")?.remove();
    if(bubbleState){
      bubbleState.cleanup?.();
      bubbleState=null;
    }
    stale?.remove();
  }

  function showCenteredMessage(options){
    hideBubble();
    const bubble=document.createElement("div");
    bubble.id=options.elementId;
    bubble.setAttribute("role","status");

    const handle=document.createElement("div");
    handle.dataset.dapDragHandle="1";
    handle.setAttribute("aria-label",options.dragText);
    handle.title=options.dragText;
    handle.textContent="⠿";
    Object.assign(handle.style,{
      display:"block",width:"fit-content",marginLeft:"auto",marginRight:"auto",
      textAlign:"center",fontSize:"18px",lineHeight:"14px",opacity:".72",
      marginBottom:"6px",cursor:"grab",touchAction:"none"
    });
    bubble.appendChild(handle);

    if(options.progressText){
      const progress=document.createElement("div");
      progress.textContent=options.progressText;
      Object.assign(progress.style,{
        fontSize:"12px",opacity:".78",marginBottom:"6px",fontWeight:"600",cursor:"default"
      });
      bubble.appendChild(progress);
    }

    const content=document.createElement("div");
    content.textContent=options.content;
    content.style.cursor="default";
    bubble.appendChild(content);

    const button=document.createElement("button");
    button.type="button";
    button.textContent=options.actionText;
    button.dataset[options.actionDataKey]="1";
    Object.assign(button.style,{
      marginTop:"12px",padding:"7px 14px",borderRadius:"6px",
      border:"1px solid "+theme.borderColor,background:"#FFFFFF",
      color:theme.backgroundColor,fontWeight:"700",cursor:"pointer"
    });
    bubble.appendChild(button);

    Object.assign(bubble.style,{
      position:"fixed",zIndex:"2147483647",maxWidth:theme.maxWidth+"px",
      padding:theme.padding,background:theme.backgroundColor,color:theme.textColor,
      border:theme.borderWidth+"px solid "+theme.borderColor,
      borderRadius:theme.borderRadius+"px",boxShadow:theme.boxShadow,
      fontFamily:theme.fontFamily,fontSize:theme.fontSize+"px",
      lineHeight:String(theme.lineHeight),direction:"rtl",
      left:"50%",top:"50%",transform:"translate(-50%,-50%)",
      cursor:"default",touchAction:"none",userSelect:"none"
    });

    let drag=null;
    const clamp=(x,y)=>{
      const q=bubble.getBoundingClientRect(),m=8;
      return{
        x:Math.max(m,Math.min(x,Math.max(m,innerWidth-q.width-m))),
        y:Math.max(m,Math.min(y,Math.max(m,innerHeight-q.height-m)))
      };
    };
    const beginDrag=e=>{
      if(e.button!==0||!e.target.closest('[data-dap-drag-handle="1"]'))return;
      const q=bubble.getBoundingClientRect();
      bubble.style.transform="";
      drag={id:e.pointerId,x:e.clientX,y:e.clientY,left:q.left,top:q.top};
      bubble.setPointerCapture(e.pointerId);
      handle.style.cursor="grabbing";
      e.preventDefault();
    };
    const move=e=>{
      if(!drag||e.pointerId!==drag.id)return;
      const next=clamp(drag.left+e.clientX-drag.x,drag.top+e.clientY-drag.y);
      bubble.style.left=next.x+"px";bubble.style.top=next.y+"px";
    };
    const finish=e=>{
      if(!drag||e.pointerId!==drag.id)return;
      drag=null;handle.style.cursor="grab";
      try{bubble.releasePointerCapture(e.pointerId)}catch{}
    };
    bubble.addEventListener("pointerdown",beginDrag);
    bubble.addEventListener("pointermove",move);
    bubble.addEventListener("pointerup",finish);
    bubble.addEventListener("pointercancel",finish);

    button.addEventListener("click",()=>{
      bubble.remove();
      void chrome.runtime.sendMessage({
        type:"dap-adapter-event",
        payload:{type:options.eventType,stepId:options.stepId}
      }).catch(()=>{});
    },{once:true});

    document.body.appendChild(bubble);
    button.focus({preventScroll:true});
    return bubble;
  }

  function showBubbleProxy(step,stepNumber,totalSteps,targetRect){
    document.getElementById("dap-guide-bubble-proxy")?.remove();
    const bubble=document.createElement("div");
    bubble.id="dap-guide-bubble-proxy";
    bubble.dataset.dapStepId=step.id;
    const handle=document.createElement("div");
    handle.dataset.dapDragHandle="1";
    handle.textContent="⠿";
    handle.title="גרור בועית";
    Object.assign(handle.style,{display:"block",width:"fit-content",marginLeft:"auto",marginRight:"auto",textAlign:"center",fontSize:"18px",lineHeight:"14px",opacity:".72",marginBottom:"6px",cursor:"grab",touchAction:"none"});
    bubble.appendChild(handle);
    const content=document.createElement("div");
    content.textContent=step.bubble?.content||"";
    content.style.cursor="default";
    bubble.appendChild(content);
    if(stepNumber&&totalSteps){
      const progress=document.createElement("div");
      progress.textContent="שלב "+stepNumber+" מתוך "+totalSteps;
      Object.assign(progress.style,{fontSize:"12px",opacity:".78",marginTop:"8px",fontWeight:"600",cursor:"default"});
      bubble.appendChild(progress);
    }
    Object.assign(bubble.style,{position:"fixed",zIndex:"2147483647",maxWidth:theme.maxWidth+"px",padding:theme.padding,background:theme.backgroundColor,color:theme.textColor,border:theme.borderWidth+"px solid "+theme.borderColor,borderRadius:theme.borderRadius+"px",boxShadow:theme.boxShadow,fontFamily:theme.fontFamily,fontSize:theme.fontSize+"px",lineHeight:String(theme.lineHeight),direction:"rtl",pointerEvents:"auto",visibility:"hidden",cursor:"default",touchAction:"none",userSelect:"none"});
    document.body.appendChild(bubble);
    const q=bubble.getBoundingClientRect(),margin=8,gap=8;
    const centerX=targetRect.x+targetRect.width/2;
    const bottomY=targetRect.y+targetRect.height;
    const left=Math.max(margin,Math.min(centerX-q.width/2,innerWidth-q.width-margin));
    let top=bottomY+gap;
    if(top+q.height>innerHeight-margin)top=Math.max(margin,targetRect.y-q.height-gap);
    bubble.style.left=left+"px";
    bubble.style.top=Math.max(margin,Math.min(top,innerHeight-q.height-margin))+"px";
    bubble.style.visibility="visible";

    let drag=null;
    const clamp=(x,y)=>({x:Math.max(margin,Math.min(x,innerWidth-q.width-margin)),y:Math.max(margin,Math.min(y,innerHeight-q.height-margin))});
    bubble.addEventListener("pointerdown",e=>{if(e.button!==0||!e.target.closest('[data-dap-drag-handle="1"]'))return;const r=bubble.getBoundingClientRect();drag={id:e.pointerId,x:e.clientX,y:e.clientY,left:r.left,top:r.top};bubble.setPointerCapture(e.pointerId);handle.style.cursor="grabbing";e.preventDefault();e.stopPropagation()});
    bubble.addEventListener("pointermove",e=>{if(!drag||e.pointerId!==drag.id)return;const n=clamp(drag.left+e.clientX-drag.x,drag.top+e.clientY-drag.y);bubble.style.left=n.x+"px";bubble.style.top=n.y+"px";e.preventDefault();e.stopPropagation()});
    const finish=e=>{if(!drag||e.pointerId!==drag.id)return;drag=null;handle.style.cursor="grab";try{bubble.releasePointerCapture(e.pointerId)}catch{}e.preventDefault();e.stopPropagation()};
    bubble.addEventListener("pointerup",finish);
    bubble.addEventListener("pointercancel",finish);
    return {status:"resolved",count:1};
  }
  function showBubble(step,stepNumber,totalSteps){
    hideBubble();const z=resolveTarget(step.target);if(z.status!=="resolved")return z;const el=z.element,root=el.ownerDocument;
    // Mirror WebBubblePresenter: never allow a stale bubble from an older
    // extension context to coexist with the current Step presentation.
    const existing=root.getElementById("dap-guide-bubble");
    existing?.__dapCleanup?.();
    existing?.remove();
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
    const place=()=>{if(manual){const q=b.getBoundingClientRect(),n=clamp(q.left,q.top);b.style.left=n.x+"px";b.style.top=n.y+"px";b.style.visibility="visible";return}b.style.visibility="hidden";if(!el.isConnected)return;const r=el.getBoundingClientRect(),q=b.getBoundingClientRect(),gap=theme.pointerSize+8;if(!(r.width>0&&r.height>0&&r.bottom>0&&r.right>0&&r.top<innerHeight&&r.left<innerWidth))return;const coords=x=>x==="Top"?[r.left+(r.width-q.width)/2,r.top-q.height-gap]:x==="Left"?[r.left-q.width-gap,r.top+(r.height-q.height)/2]:x==="Right"?[r.right+gap,r.top+(r.height-q.height)/2]:[r.left+(r.width-q.width)/2,r.bottom+gap];const preferred=String(step.bubble?.placement||"Auto");const sides=[preferred==="Auto"?"Bottom":preferred,"Top","Right","Left","Bottom"].filter((x,i,a)=>a.indexOf(x)===i);const candidates=sides.map(side=>{let[x,y]=coords(side);if(side==="Top"||side==="Bottom")x=Math.max(margin,Math.min(x,innerWidth-q.width-margin));else y=Math.max(margin,Math.min(y,innerHeight-q.height-margin));const inside=x>=margin&&y>=margin&&x+q.width<=innerWidth-margin&&y+q.height<=innerHeight-margin;const overlap=!(x+q.width<=r.left||x>=r.right||y+q.height<=r.top||y>=r.bottom);const overflow=Math.max(0,margin-x)+Math.max(0,margin-y)+Math.max(0,x+q.width-(innerWidth-margin))+Math.max(0,y+q.height-(innerHeight-margin));return{side,x,y,inside,overlap,overflow}});let chosen=candidates.find(x=>x.inside&&!x.overlap);if(!chosen){const safe=candidates.filter(x=>!x.overlap).sort((a,z)=>a.overflow-z.overflow);chosen=safe[0]}if(!chosen||!chosen.inside){b.dataset.actualPlacement="Overlay";b.style.pointerEvents="none";pointer.style.display="none";return}b.style.pointerEvents="";b.style.left=chosen.x+"px";b.style.top=chosen.y+"px";pointer.style.display="";pointerFor(chosen.side);b.style.visibility="visible";b.dataset.actualPlacement=chosen.side};
    const down=e=>{if(e.button!==0||!e.target.closest('[data-dap-drag-handle="1"]'))return;const q=b.getBoundingClientRect();drag={id:e.pointerId,x:e.clientX,y:e.clientY,left:q.left,top:q.top};b.setPointerCapture(e.pointerId);handle.style.setProperty("cursor","grabbing","important");e.preventDefault()};const move=e=>{if(!drag||e.pointerId!==drag.id)return;const n=clamp(drag.left+e.clientX-drag.x,drag.top+e.clientY-drag.y);b.style.left=n.x+"px";b.style.top=n.y+"px"};const up=e=>{if(!drag||e.pointerId!==drag.id)return;manual=true;drag=null;handle.style.setProperty("cursor","grab","important");pointer.style.display="none";b.dataset.manualPosition="true";try{b.releasePointerCapture(e.pointerId)}catch{}};
    b.addEventListener("pointerdown",down);b.addEventListener("pointermove",move);b.addEventListener("pointerup",up);b.addEventListener("pointercancel",up);const ro=new ResizeObserver(place);ro.observe(el);ro.observe(b);addEventListener("scroll",place,true);addEventListener("resize",place);place();
    bubbleState={bubble:b,step,element:el,stepNumber,totalSteps,cleanup:()=>{ro.disconnect();removeEventListener("scroll",place,true);removeEventListener("resize",place);b.remove();el.style.outline=previous.outline;el.style.outlineOffset=previous.outlineOffset;el.style.boxShadow=previous.boxShadow}};
    let topRect=null;
    if(b.dataset.actualPlacement==="Overlay"){
      try{
        let x=z.rect.x,y=z.rect.y,w=window;
        while(w!==w.top){
          const frame=w.frameElement;
          if(!frame)break;
          const fr=frame.getBoundingClientRect();
          x+=fr.left;y+=fr.top;w=w.parent;
        }
        topRect={x,y,width:z.rect.width,height:z.rect.height};
      }catch{}
    }
    return{status:"resolved",count:1,rect:z.rect,needsTopLevel:b.dataset.actualPlacement==="Overlay",topRect};
  }
  function reconcile(){
    if(!bubbleState)return;
    const z=resolveTarget(bubbleState.step.target);
    if(z.status!=="resolved"){hideBubble();return}
    if(z.element!==bubbleState.element){
      const step=bubbleState.step;
      const stepNumber=bubbleState.stepNumber;
      const totalSteps=bubbleState.totalSteps;
      showBubble(step,stepNumber,totalSteps);
    }
  }
  function inputValue(el){return "value" in el ? String(el.value ?? "") : "";}
  function validationKind(step){return String(step?.validation?.kind??step?.validation?.Kind??"").toLowerCase();}
  function emitAdapterEvent(type,step,armId,extra={}){
    return chrome.runtime.sendMessage({
      type:"dap-adapter-event",
      payload:{
        type,
        stepId:step?.id??null,
        armId:armId??null,
        documentHasFocus:document.hasFocus(),
        targetIsActive:document.activeElement===validationState?.element,
        ...extra
      }
    });
  }

  function armValidationTarget(step, element, armId) {
    const kind = validationKind(step);
    element.__dapValidationArmId = armId ?? null;
    validationState = {step,element,armId:armId??null};

    if (kind === "clicked") {
      if (element.__dapValidationClickHandler)
        element.removeEventListener("click", element.__dapValidationClickHandler, true);

      const clickHandler = event => {
        hideBubble();

        const currentArmId = element.__dapValidationArmId;
        const tag = element.tagName?.toLowerCase();
        const type = (element.getAttribute?.("type") || "").toLowerCase();
        const form = element.form;
        const defersDefault =
          event.cancelable &&
          ((tag === "a" && !!element.getAttribute("href")) ||
           (tag === "button" && form && (!type || type === "submit")) ||
           (tag === "input" && form && (type === "submit" || type === "image")));

        const report = () => emitAdapterEvent(
          "validation-commit",
          step,
          currentArmId,
          {kind:"clicked",browserEvent:"click"}
        ).catch(()=>null);

        if (!defersDefault) {
          void report();
          return;
        }

        event.preventDefault();
        void report().then(() => {
          if (!element.isConnected) return;
          if (tag === "a") {
            const href = element.getAttribute("href");
            if (href) location.href = href;
            return;
          }
          if (form) form.requestSubmit(element);
        });
      };

      element.__dapValidationClickHandler = clickHandler;
      element.addEventListener("click", clickHandler, {capture:true});
      return;
    }

    if (!kind) return;

    if (element.__dapValidationStepId !== step.id) {
      if (element.__dapValidationCommitHandler)
        element.removeEventListener(element.__dapValidationCommitEvent, element.__dapValidationCommitHandler, true);
      if (element.__dapValidationInputHandler)
        element.removeEventListener("input", element.__dapValidationInputHandler, true);
      if (element.__dapValidationChangeHandler)
        element.removeEventListener("change", element.__dapValidationChangeHandler, true);

      const tag = element.tagName?.toLowerCase();
      const type = (element.getAttribute?.("type") || "").toLowerCase();
      const isTextEditor = tag === "textarea" ||
        (tag === "input" && !["checkbox","radio","button","submit","reset"].includes(type));
      const eventName = isTextEditor ? "blur" : "change";
      const state = {changed:false};

      if (isTextEditor) {
        const markChanged = () => { state.changed = true; };
        element.__dapValidationInputHandler = markChanged;
        element.__dapValidationChangeHandler = markChanged;
        element.addEventListener("input", markChanged, {capture:true});
        element.addEventListener("change", markChanged, {capture:true});
      }

      const commit = event => {
        if (isTextEditor && !state.changed) return;
        state.changed = false;
        const currentArmId = element.__dapValidationArmId;
        void emitAdapterEvent(
          "validation-commit",
          step,
          currentArmId,
          {kind,browserEvent:event?.type||eventName}
        ).catch(()=>{});
      };

      element.__dapValidationStepId = step.id;
      element.__dapValidationCommitHandler = commit;
      element.__dapValidationCommitEvent = eventName;
      element.addEventListener(eventName, commit, {capture:true});
    }
  }

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

  const existingRuntime = globalThis.__dapWebRuntime;
  globalThis.__dapWebRuntime = {
    version: "0.3.0",
    resolveTarget,
    showBubble,
    hideBubble,
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
