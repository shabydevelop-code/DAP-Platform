(() => {
  if (globalThis.__guideMeCaptureInstalled) return;
  globalThis.__guideMeCaptureInstalled = true;

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

  function report(event) {
    if (!event.isTrusted) return;
    const element = event.target instanceof Element ? event.target : null;
    if (!element) return;
    const target = element.closest("button,a,input,select,textarea,[role='button']") || element;
    const before = snapshot();
    const details = {
      kind: event.type,
      url: location.href,
      tag: target.tagName.toLowerCase(),
      label: String(target.getAttribute("aria-label") || target.getAttribute("name") || target.textContent || "").trim().slice(0, 120),
      id: target.id || null
    };
    // Allow SPA handlers to update the DOM before recording the outcome.
    setTimeout(() => {
      chrome.runtime.sendMessage({
        type: "guideme-instructor-event",
        event: { ...details, before, after: snapshot() }
      }).catch(() => {});
    }, 600);
  }

  addEventListener("click", report, true);
  addEventListener("change", report, true);
})();
