// Instructor transport is separate from the production Native Messaging channel.
const captureEndpoint = "http://127.0.0.1:53921/event";
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "guideme-instructor-pair") {
    const token = String(message.token || "");
    if (!/^[a-f0-9]{32}$/i.test(token) || !Number.isInteger(message.tabId)) {
      sendResponse({ ok: false, error: "Invalid pairing request" });
      return false;
    }
    chrome.storage.session.set({ instructorPairing: { token, tabId: message.tabId } })
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: String(error) }));
    return true;
  }
  if (message?.type === "guideme-instructor-stop") {
    chrome.storage.session.remove("instructorPairing")
      .then(() => sendResponse({ ok: true }));
    return true;
  }
  if (message?.type !== "guideme-instructor-event") return false;
  if (!sender.tab?.id || !sender.url?.startsWith("http")) return false;
  chrome.storage.session.get("instructorPairing").then(async state => {
    const pairing = state.instructorPairing;
    if (!pairing || pairing.tabId !== sender.tab.id) return;
    const event = message.event;
    if (!event || !["click", "change", "blur"].includes(event.kind) || typeof event.url !== "string") return;
    await fetch(captureEndpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-GuideMe-Capture-Token": pairing.token
      },
      body: JSON.stringify(event)
    });
  }).catch(() => {});
  return false;
});
