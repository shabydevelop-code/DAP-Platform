// Instructor transport is separate from the production Native Messaging channel.
const captureEndpoint = "http://127.0.0.1:53921/event";
const heartbeatEndpoint = "http://127.0.0.1:53921/heartbeat";
async function heartbeat(token) {
  const response = await fetch(heartbeatEndpoint, { method: "POST", headers: { "X-GuideMe-Capture-Token": token } });
  return response.ok;
}
async function pairAutomatically(tabId) {
  const response = await fetch("http://127.0.0.1:53921/pair", { method: "POST" });
  if (!response.ok) throw new Error("Instructor is unavailable");
  const { token } = await response.json();
  if (!/^[a-f0-9]{32}$/i.test(token) || !(await heartbeat(token)))
    throw new Error("Instructor rejected connection");
  await chrome.storage.session.set({ instructorPairing: { token, tabId } });
  return { ok: true };
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "guideme-instructor-auto-pair") {
    if (!Number.isInteger(message.tabId)) { sendResponse({ ok: false, error: "Invalid tab" }); return false; }
    pairAutomatically(message.tabId).then(sendResponse).catch(error => sendResponse({ ok: false, error: String(error.message || error) }));
    return true;
  }
  if (message?.type === "guideme-instructor-pair") {
    const token = String(message.token || "");
    if (!/^[a-f0-9]{32}$/i.test(token) || !Number.isInteger(message.tabId)) {
      sendResponse({ ok: false, error: "Invalid pairing request" });
      return false;
    }
    heartbeat(token).then(async ok => {
        if (!ok) throw new Error("Instructor rejected pairing");
        await chrome.storage.session.set({ instructorPairing: { token, tabId: message.tabId } });
        sendResponse({ ok: true });
      })
      .catch(error => sendResponse({ ok: false, error: String(error) }));
    return true;
  }
  if (message?.type === "guideme-instructor-stop") {
    chrome.storage.session.remove("instructorPairing")
      .then(() => sendResponse({ ok: true }));
    return true;
  }
  if (message?.type === "guideme-instructor-heartbeat") {
    chrome.storage.session.get("instructorPairing").then(async state => {
      const pairing = state.instructorPairing;
      if (!pairing || pairing.tabId !== sender.tab?.id) return;
      const ok = await heartbeat(pairing.token);
      if (!ok) await chrome.storage.session.remove("instructorPairing");
    }).catch(() => {});
    return false;
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
