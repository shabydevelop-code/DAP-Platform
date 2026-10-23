(() => {
  // Replace any capture listener from an earlier injection in this document.
  try { globalThis.__guideMeCaptureCleanup?.(); } catch { /* stale context */ }
  globalThis.__guideMeCaptureInstalled = true;

  let active = true;
  let pickActive = false;
  let highlighted = null;
  const pendingTimers = new Set();
  const eventTypes = ["click", "change", "blur"];

  function stopCapture() {
    if (!active) return;
    active = false;
    for (const type of eventTypes) removeEventListener(type, report, true);
    removeEventListener("click", pickClick, true);
    chrome.runtime.onMessage.removeListener(pickMessages);
    if (highlighted) highlighted.style.outline = "";
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

  function pickMessages(message, _sender, sendResponse) {
    if (message?.type === "guideme-instructor-pick-start") {
      pickActive = true;
      sendResponse({armed: true, url: location.href});
      return false;
    }
    if (message?.type === "guideme-instructor-pick-stop") {
      pickActive = false;
      if (highlighted) highlighted.style.outline = "";
      highlighted = null;
      sendResponse({armed: false});
      return false;
    }
  }
  function pickClick(event) {
    if (!pickActive || !event.isTrusted) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;
    const selector = selectorFor(target);
    if (!selector) return;
    pickActive = false;
    target.style.outline = "3px solid #3678e8";
    highlighted = target;
    setTimeout(() => {
      if (highlighted === target) { target.style.outline = ""; highlighted = null; }
    }, 1500);
    const candidate = {
      selector, url:location.href, tag:target.tagName.toLowerCase(),
      label:String(target.getAttribute("aria-label") || target.textContent || "").trim().slice(0,100)
    };
    try { chrome.runtime.sendMessage({type:"guideme-instructor-picked", event:candidate}).catch(() => {}); }
    catch {}
  }
  chrome.runtime.onMessage.addListener(pickMessages);
  addEventListener("click", pickClick, true);

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
