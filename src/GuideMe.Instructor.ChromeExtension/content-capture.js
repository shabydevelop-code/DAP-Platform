(() => {
  if (globalThis.__guideMeCaptureInstalled) return;
  globalThis.__guideMeCaptureInstalled = true;

  let active = true;
  const eventTypes = ["click", "change", "blur"];

  function stopCapture() {
    if (!active) return;
    active = false;
    for (const type of eventTypes) removeEventListener(type, report, true);
    globalThis.__guideMeCaptureInstalled = false;
  }

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
    if (!event.isTrusted || !extensionAvailable()) return;
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
      frameSelector: (() => { try { const frame = window.frameElement; return frame?.id ? "#" + CSS.escape(frame.id) : null; } catch { return null; } })(),
      value: event.type === "change" || event.type === "blur" ? safeFieldValue(target) : null
    };
    // Allow SPA handlers to update the DOM before recording the outcome.
    setTimeout(() => {
      if (!extensionAvailable()) return;
      try {
        const pending = chrome.runtime.sendMessage({
          type: "guideme-instructor-event",
          event: { ...details, before, after: snapshot() }
        });
        if (pending && typeof pending.catch === "function") {
          pending.catch(() => {
            // A reload can invalidate the context after sendMessage was initiated.
            if (!extensionAvailable()) stopCapture();
          });
        }
      } catch {
        stopCapture();
      }
    }, 600);
  }

  for (const type of eventTypes) addEventListener(type, report, true);
})();
