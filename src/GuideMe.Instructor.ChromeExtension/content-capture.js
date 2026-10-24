(() => {
  // Replace any capture listener from an earlier injection in this document.
  try { globalThis.__guideMeCaptureCleanup?.(); } catch { /* stale context */ }
  globalThis.__guideMeCaptureInstalled = true;

  let active = true;
  let pickActive = false;
  let highlighted = null;
  let previousOutline = null;
  let hoverPoll = null;
  let pickerNonce = null;
  const pendingTimers = new Set();
  const eventTypes = ["click", "change", "blur"];

  function stopCapture() {
    if (!active) return;
    active = false;
    globalThis.__guideMePickerActive = false;
    for (const type of eventTypes) removeEventListener(type, report, true);
    removeEventListener("pointermove", pickHover, true);
    removeEventListener("mousemove", pickHover, true);
    removeEventListener("keydown", pickKey, true);
    chrome.runtime.onMessage.removeListener(pickMessages);
    removeEventListener("message", frameSelection, false);
    clearInterval(hoverPoll);
    hoverPoll = null;
    clearHighlight();
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
    const tag = element.tagName.toLowerCase();
    const unique = selector => {
      try { return document.querySelector(selector) === element &&
        document.querySelectorAll(selector).length === 1; } catch { return false; }
    };
    const stable = value => typeof value === 'string' && value.length > 0 &&
      value.length <= 120 && !/^[a-f0-9]{16,}$/i.test(value);
    const classNames = node => [...node.classList].filter(name =>
      stable(name) && !/^(active|hover|focus|selected|disabled|loading|open|hidden|ng-|css-|jsx-|sc-)/i.test(name)
      && !/[0-9a-f]{8,}/i.test(name)).slice(0, 3);
    const simple = node => {
      const base = node.tagName.toLowerCase();
      const names = classNames(node);
      return [...names.map(name => base + '.' + CSS.escape(name)), base];
    };
    if (stable(element.id)) {
      const selector = '#' + CSS.escape(element.id);
      if (unique(selector)) return selector;
    }
    for (const attr of ['data-testid', 'data-test', 'data-qa', 'name', 'aria-label', 'placeholder']) {
      const value = element.getAttribute(attr);
      if (!stable(value)) continue;
      const selector = tag + '[' + attr + '="' + CSS.escape(value) + '"]';
      if (unique(selector)) return selector;
    }
    // Prefer an identifying class over DOM structure when it is unique.
    for (const selector of simple(element)) {
      if (unique(selector)) return selector;
    }
    // An ancestor's stable ID plus the target's distinguishing classes
    // survives insertion or removal of intermediate layout wrappers.
    for (let ancestor = element.parentElement, depth = 1;
         ancestor && depth <= 7; ancestor = ancestor.parentElement, depth++) {
      if (!stable(ancestor.id)) continue;
      const anchor = '#' + CSS.escape(ancestor.id);
      for (const suffix of simple(element)) {
        const selector = anchor + ' ' + suffix;
        if (unique(selector)) return selector;
      }
    }
    // Use structural paths only if semantic selectors cannot identify one node.
    const parts = [];
    let node = element;
    for (let depth = 0; node && node.nodeType === 1 && depth < 7; depth++, node = node.parentElement) {
      let part = node.tagName.toLowerCase();
      if (node.id && stable(node.id)) {
        part += '#' + CSS.escape(node.id);
        parts.unshift(part);
        const candidate = parts.join(' > ');
        if (unique(candidate)) return candidate;
        break;
      }
      const parent = node.parentElement;
      if (parent) {
        const siblings = [...parent.children].filter(el => el.tagName === node.tagName);
        if (siblings.length > 1)
          part += ':nth-of-type(' + (siblings.indexOf(node) + 1) + ')';
      }
      parts.unshift(part);
      const candidate = parts.join(' > ');
      if (unique(candidate)) return candidate;
    }
    const selector = parts.join(' > ');
    return unique(selector) ? selector : null;
  }

  // Outline is painted by the browser on the actual element's border box.
  // Unlike a separate positioned overlay it cannot drift when a frame scrolls,
  // has borders, or applies zoom/transforms. Outline does not affect layout.
  function clearHighlight() {
    if (highlighted && previousOutline) {
      for (const property of ['outline', 'outline-offset']) {
        const old = previousOutline[property];
        if (old.value)
          highlighted.style.setProperty(property, old.value, old.priority);
        else
          highlighted.style.removeProperty(property);
      }
    }
    highlighted = null;
    previousOutline = null;
  }

  function showHighlight(target) {
    if (target === highlighted) return;
    clearHighlight();
    highlighted = target;
    previousOutline = {};
    for (const property of ['outline', 'outline-offset']) {
      previousOutline[property] = {
        value: target.style.getPropertyValue(property),
        priority: target.style.getPropertyPriority(property)
      };
    }
    target.style.setProperty('outline', '2px solid #3678e8', 'important');
    target.style.setProperty('outline-offset', '0px', 'important');
  }

  // Relay a selected element through its parent frames. The parent determines
  // the iframe locator from event.source, so cross-origin children work without
  // DOM access across origins. The top frame delivers the complete path.
  const bridgeType = 'guideme-instructor-frame-selection-v1';
  function frameSelection(event) {
    if (!pickActive || !pickerNonce || event.data?.type !== bridgeType ||
        event.data.nonce !== pickerNonce) return;
    const childFrame = [...document.querySelectorAll('iframe,frame')]
      .find(frame => frame.contentWindow === event.source);
    if (!childFrame) return;
    const selector = selectorFor(childFrame);
    if (!selector) return;
    const payload = event.data.payload;
    if (!payload || !Array.isArray(payload.framePath) ||
        payload.framePath.length >= 12 || typeof payload.selector !== 'string') return;
    const next = {...payload, framePath:[selector,...payload.framePath]};
    emitSelection(next);
  }
  function emitSelection(payload) {
    if (window === window.top) {
      try { chrome.runtime.sendMessage({type:'guideme-instructor-picked',event:{...payload,url:location.href}}).catch(()=>{}); } catch {}
    } else {
      try { window.parent.postMessage({type:bridgeType, nonce:pickerNonce, payload}, '*'); } catch {}
    }
  }
  addEventListener('message', frameSelection, false);

  function pickMessages(message, _sender, sendResponse) {
    if (message?.type === "guideme-instructor-pick-start") {
      pickActive = true;
      globalThis.__guideMePickerActive = true;
      pickerNonce = message.nonce;
      if (!hoverPoll) hoverPoll = setInterval(inspectHoveredElement, 120);
      inspectHoveredElement();
      sendResponse({armed: true, url: location.href});
      return false;
    }
    if (message?.type === "guideme-instructor-pick-confirm") {
      confirmHighlightedSelection();
      sendResponse({handled: true});
      return false;
    }
    if (message?.type === "guideme-instructor-pick-stop") {
      pickActive = false;
      globalThis.__guideMePickerActive = false;
      pickerNonce = null;
      clearInterval(hoverPoll);
      hoverPoll = null;
      clearHighlight();
      sendResponse({armed: false});
      return false;
    }
  }
  function inspectHoveredElement() {
    if (!pickActive) return;
    const hovered = document.querySelectorAll(':hover');
    const element = hovered.length ? hovered[hovered.length - 1] : null;
    if (!(element instanceof Element)) return;
    // The child iframe owns its own highlight. Never paint its entire viewport.
    if (['HTML','BODY','IFRAME','FRAME','HEADER','MAIN'].includes(element.tagName)) {
      clearHighlight();
      return;
    }
    const target = element.closest('button,a,input,select,textarea,[role="button"],[role="textbox"],[role="link"]') || element;
    if (target !== highlighted) showHighlight(target);
  }
  function pickHover(event) {
    if (!pickActive || !event.isTrusted) return;
    inspectHoveredElement();
  }
  function pickKey(event) {
    if (!pickActive || !event.isTrusted) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopImmediatePropagation();
      pickActive = false;
      globalThis.__guideMePickerActive = false;
      clearInterval(hoverPoll);
      hoverPoll = null;
      clearHighlight();
      highlighted = null;
      try { chrome.runtime.sendMessage({type:'guideme-instructor-pick-cancelled'}).catch(()=>{}); } catch {}
      return;
    }
    if (!(event.ctrlKey && event.shiftKey && event.code === 'Space')) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    confirmHighlightedSelection();
  }
  function confirmHighlightedSelection() {
    if (!pickActive) return;
    // Refresh the hovered element even when the page has not received focus.
    inspectHoveredElement();
    if (!highlighted || !highlighted.isConnected) return;
    const target = highlighted;
    const selector = selectorFor(target);
    if (!selector) {
      try { chrome.runtime.sendMessage({type:"guideme-instructor-pick-error",reason:"לא ניתן ליצור מזהה CSS ייחודי לרכיב המסומן."}).catch(()=>{}); } catch {}
      return;
    }
    pickActive = false;
    globalThis.__guideMePickerActive = false;
    clearInterval(hoverPoll);
    hoverPoll = null;
    clearHighlight();
    // Offer stable grid-row identity by exact cell value, independent of row order.
    // Only offer values that uniquely identify a row in the selected table.
    const row = target.closest('tr');
    const table = row?.closest('table');
    const gridChoices = [];
    if (row && table && row.closest('tbody') && row.cells.length) {
      const tableSelector = selectorFor(table);
      if (tableSelector) {
        const rows = [...table.querySelectorAll('tbody tr')];
        const headers = [...table.querySelectorAll('thead th')];
        [...row.cells].forEach((cell, index) => {
          const value = cell.textContent.replace(/\\s+/g, ' ').trim();
          if (!value || value.length > 120 || /[\\r\\n]/.test(value)) return;
          const matches = rows.filter(other =>
            other.cells[index]?.textContent.replace(/\\s+/g, ' ').trim() === value);
          if (matches.length !== 1 || matches[0] !== row) return;
          const header = headers[index]?.textContent.replace(/\\s+/g, ' ').trim().slice(0, 60) || 'עמודה ' + (index + 1);
          const rowSelector = tableSelector + ' tbody tr:cell-text(' + (index + 1) + ',' + JSON.stringify(value) + ')';
          if (rowSelector.length <= 400) gridChoices.push({header, value, selector:rowSelector});
        });
      }
    }
    const candidate = {
      gridChoices,
      selector, url:location.href, tag:target.tagName.toLowerCase(),
      label:(() => {
        const explicit = target.getAttribute("aria-label") ||
          target.getAttribute("title") || target.getAttribute("name");
        if (explicit) return String(explicit).trim().slice(0,80);
        // A container's textContent includes all descendant records and may
        // contain private business data. Never use it as an element label.
        const directText = [...target.childNodes]
          .filter(node => node.nodeType === Node.TEXT_NODE)
          .map(node => node.textContent.trim()).filter(Boolean).join(" ").trim();
        return (directText && directText.length <= 80 ? directText :
          (target.id ? "#" + CSS.escape(target.id) :
            target.tagName.toLowerCase())).slice(0,80);
      })(),
      framePath: []
    };
    emitSelection(candidate);
  }

  chrome.runtime.onMessage.addListener(pickMessages);
  addEventListener("pointermove", pickHover, true);
  addEventListener("mousemove", pickHover, true);
  addEventListener("keydown", pickKey, true);

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
            event: { ...details, contextCandidate: details.contextCandidate && document.querySelectorAll(details.contextCandidate).length === 1 ? details.contextCandidate : null }
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
