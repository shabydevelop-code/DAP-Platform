(() => {
  if (globalThis.__guideMeCaptureInstalled) return;
  globalThis.__guideMeCaptureInstalled = true;
  function report(event) {
    if (!event.isTrusted) return;
    const element = event.target instanceof Element ? event.target : null;
    if (!element) return;
    const target = element.closest("button,a,input,select,textarea,[role='button']") || element;
    const message = {
      type: "guideme-instructor-event",
      event: {
        kind: event.type,
        url: location.href,
        tag: target.tagName.toLowerCase(),
        label: String(target.getAttribute("aria-label") || target.getAttribute("name") || target.textContent || "").trim().slice(0, 120),
        id: target.id || null
      }
    };
    chrome.runtime.sendMessage(message).catch(() => {});
  }
  addEventListener("click", report, true);
  addEventListener("change", report, true);
})();
