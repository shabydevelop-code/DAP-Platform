(() => {
  // Replace any capture listener from an earlier injection in this document.
  try { globalThis.__guideMeCaptureCleanup?.(); } catch { /* stale context */ }
  globalThis.__guideMeCaptureInstalled = true;

  let active = true;
  let pickActive = false;
  let highlighted = null;
  let overlay = null;
  let label = null;
  let inspectorBanner = null;
  const pendingTimers = new Set();
  const eventTypes = ["click", "change", "blur"];

  function stopCapture() {
    if (!active) return;
    active = false;
    for (const type of eventTypes) removeEventListener(type, report, true);
    removeEventListener("pointermove", pickHover, true);
    removeEventListener("mousemove", pickHover, true);
    removeEventListener("keydown", pickKey, true);
    chrome.runtime.onMessage.removeListener(pickMessages);
    clearHighlight();
    inspectorBanner?.remove();
    inspectorBanner = null;
    for (const timer of pendingTimers) clearTimeout(timer);
    pendingTimers.clear();
    globalThis.__guideMeCaptureInstalled = false;
    if (globalThis.__guideMeCaptureCleanup === stopCapture)
      delete globalThis.__guideMeCaptureCleanup;
  }

  globalThis.__guideMeCaptureCleanup = stopCapture;

  function extensionAvailable() {
    if (!active) return false;
    try {
      if (!chrome?.runtime?.id) {
        stopCapture();
        return false;
      }
      return true;
    } catch {
      stopCapture();
      return false;
    }
  }

  function selectorFor(element) {
    if (element.id) {
      const selector = '#' + CSS.escape(element.id);
      if (document.querySelectorAll(selector).length === 1) return selector;
    }
    const parts = [];
    let node = element;
    for (let depth = 0; node && node.nodeType === 1 && depth < 7; depth++, node = node.parentElement) {
      let part = node.tagName.toLowerCase();
      if (node.id) {
        part += '#' + CSS.escape(node.id);
        parts.unshift(part);
        break;
      }
      const parent = node.parentElement;
      if (parent) {
        const siblings = [...parent.children].filter(el => el.tagName === node.tagName);
        if (siblings.length > 1) part += ':nth-of-type(' + ([...parent.children].filter(el => el.tagName === node.tagName).indexOf(node) + 1) + ')';
      }
      parts.unshift(part);
      const candidate = parts.join(' > ');
      if (document.querySelectorAll(candidate).length === 1) return candidate;
    }
    const selector = parts.join(' > ');
    return selector && document.querySelectorAll(selector).length === 1 ? selector : null;
  }

  function clearHighlight() {
    overlay?.remove();
    label?.remove();
    overlay = null;
    label = null;
    highlighted = null;
  }

  function showHighlight(target) {
    clearHighlight();
    highlighted = target;
    const rect = target.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    overlay = document.createElement('div');
    Object.assign(overlay.style, {
      position:'fixed', top:rect.top+'px',left:rect.left+'px',
      width:rect.width+'px',height:rect.height+'px',
      border:'3px solid #3678e8',background:'rgba(54,120,232,.10)',
      borderRadius:'3px',boxSizing:'border-box',
      pointerEvents:'none',zIndex:'2147483646'
    });
    label = document.createElement('div');
    const caption = target.getAttribute('aria-label') ||
      target.getAttribute('name') || target.id || target.tagName.toLowerCase();
    label.textContent = target.tagName.toLowerCase()+' · '+String(caption).slice(0,50);
    Object.assign(label.style,{
      position:'fixed',top:Math.max(0,rect.top-27)+'px',
      left:Math.max(0,rect.left)+'px',maxWidth:'240px',
      padding:'4px 7px',background:'#17385f',color:'#fff',
      borderRadius:'4px',font:'12px Arial,sans-serif',
      direction:'ltr',pointerEvents:'none',zIndex:'2147483647'
    });
    document.documentElement.append(overlay,label);
  }

  function pickMessages(message, _sender, sendResponse) {
    if (message?.type === "guideme-instructor-pick-start") {
      pickActive = true;
      if (!inspectorBanner) {
        inspectorBanner = document.createElement('div');
        inspectorBanner.textContent = 'GuideMe Instructor · העבר עכבר מעל רכיב · Ctrl+Shift+Space לבחירה · Esc לביטול';
        Object.assign(inspectorBanner.style, {
          position:'fixed',top:'12px',left:'50%',transform:'translateX(-50%)',
          maxWidth:'90vw',background:'#153c66',color:'white',borderRadius:'8px',
          padding:'10px 16px',font:'bold 13px Arial,sans-serif',
          direction:'rtl',textAlign:'center',pointerEvents:'none',
          zIndex:'2147483647',boxShadow:'0 6px 20px rgba(0,0,0,.25)'
        });
        document.documentElement.appendChild(inspectorBanner);
      }
      sendResponse({armed: true, url: location.href});
      return false;
    }
    if (message?.type === "guideme-instructor-pick-stop") {
      pickActive = false;
      clearHighlight();
      inspectorBanner?.remove();
      inspectorBanner = null;
      sendResponse({armed: false});
      return false;
    }
  }
  function pickHover(event) {
    if (!pickActive || !event.isTrusted) return;
    // Query under the cursor, rather than trusting event.target: delegated
    // handlers, nested labels and injected overlays can change event targets.
    const element = document.elementFromPoint(event.clientX, event.clientY);
    if (!(element instanceof Element)) return;
    const target = element.closest('button,a,input,select,textarea,[role="button"],[role="textbox"],[role="link"]') || element;
    if (inspectorBanner) {
      inspectorBanner.dataset.hoverSeen = 'true';
      inspectorBanner.textContent = 'GuideMe Instructor · מזוהה: ' +
        target.tagName.toLowerCase() + ' · Ctrl+Shift+Space לבחירה · Esc לביטול';
    }
    if (target === highlighted) return;
    showHighlight(target);
  }
  function pickKey(event) {
    if (!pickActive || !event.isTrusted) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopImmediatePropagation();
      pickActive = false;
      clearHighlight();
      inspectorBanner?.remove();
      inspectorBanner = null;
      highlighted = null;
      try { chrome.runtime.sendMessage({type:'guideme-instructor-pick-cancelled'}).catch(()=>{}); } catch {}
      return;
    }
    if (!(event.ctrlKey && event.shiftKey && event.code === 'Space')) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    if (!highlighted || !highlighted.isConnected) return;
    const target = highlighted;
    const selector = selectorFor(target);
    if (!selector) return;
    pickActive = false;
    clearHighlight();
    inspectorBanner?.remove();
    inspectorBanner = null;
    const candidate = {
      selector, url:location.href, tag:target.tagName.toLowerCase(),
      label:String(target.getAttribute('aria-label') || target.getAttribute('name') || target.textContent || '').trim().slice(0,100)
    };
    try { chrome.runtime.sendMessage({type:'guideme-instructor-picked', event:candidate}).catch(()=>{}); } catch {}
  }
  chrome.runtime.onMessage.addListener(pickMessages);
  addEventListener("pointermove", pickHover, true);
  addEventListener("mousemove", pickHover, true);
  addEventListener("keydown", pickKey, true);

  // Structural snapshot only: never collect input values or page text wholesale.
  function snapshot() {
    const visible = element => {
      const style = getComputedStyle(element);
      return style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0;
    };
    const count = selector => [...document.querySelectorAll(selector)].filter(visible).length;
    const labels = selector => [...document.querySelectorAll(selector)]
      .filter(visible)
      .map(element => String(element.getAttribute("aria-label") || element.textContent || "").trim().replace(/\s+/g, " ").slice(0, 70))
      .filter(Boolean).slice(0, 12);
    return {
      url: location.href,
      dialogs: count('[role="dialog"],dialog'),
      inputs: count("input:not([type=hidden]),textarea"),
      selects: count("select"),
      buttons: count('button,[role="button"]'),
      headings: labels("h1,h2,h3,[role=heading]"),
      dialogsLabels: labels('[role="dialog"],dialog')
    };
  }

  function contextCandidate(target) {
    // Prefer a unique, visible, non-generic ancestor container over the input itself.
    for (let node = target.parentElement; node && node !== document.body; node = node.parentElement) {
      const id = node.id;
      if (!id || !/^[a-zA-Z][a-zA-Z0-9_-]{2,79}$/.test(id)) continue;
      if (/^(root|app|main|content|container|wrapper|layout)$/i.test(id)) continue;
      const selector = "#" + CSS.escape(id);
      if (document.querySelectorAll(selector).length === 1) return selector;
    }
    return null;
  }

  function safeFieldValue(target) {
    if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement))
      return null;
    const type = String(target.type || "").toLowerCase();
    const autocomplete = String(target.autocomplete || "").toLowerCase();
    if (["password", "hidden", "file", "email", "tel", "number"].includes(type) ||
        /password|cc-|credit|one-time-code|otp|token|secret|email|tel|address|birth/.test(autocomplete) ||
        /password|passcode|secret|token|otp|credit|card|email|phone|mobile|address|birth/i.test(
          [target.name, target.id, target.getAttribute("aria-label")].join(" ")))
      return null;
    return String(target.value ?? "").slice(0, 160);
  }

  function report(event) {
    try {
      if (pickActive || !event.isTrusted || !extensionAvailable()) return;
      const element = event.target instanceof Element ? event.target : null;
      if (!element) return;
      const target = element.closest("button,a,input,select,textarea,[role='button']") || element;
      const before = snapshot();
      const details = {
        kind: event.type,
        url: location.href,
        tag: target.tagName.toLowerCase(),
        label: String(target.getAttribute("aria-label") || target.getAttribute("name") || target.textContent || "").trim().slice(0, 120),
        id: target.id || null,
        name: target.getAttribute("name") || null,
        contextCandidate: contextCandidate(target),
        frameSelector: (() => { try { const frame = window.frameElement; return frame?.id ? "#" + CSS.escape(frame.id) : null; } catch { return null; } })(),
        value: event.type === "change" || event.type === "blur" ? safeFieldValue(target) : null
      };
      // Allow SPA handlers to update the DOM before recording the outcome.
      const timer = setTimeout(() => {
        pendingTimers.delete(timer);
        if (!extensionAvailable()) return;
        try {
          const pending = chrome.runtime.sendMessage({
            type: "guideme-instructor-event",
            event: { ...details, contextCandidate: details.contextCandidate && document.querySelectorAll(details.contextCandidate).length === 1 ? details.contextCandidate : null, before, after: snapshot() }
          });
          if (pending && typeof pending.catch === "function") {
            pending.catch(() => {
              if (!extensionAvailable()) stopCapture();
            });
          }
        } catch {
          stopCapture();
        }
      }, 600);
      pendingTimers.add(timer);
    } catch {
      // An invalidated extension context must never break the target application.
      stopCapture();
    }
  }

  for (const type of eventTypes) addEventListener(type, report, true);
})();
