// Instructor transport is separate from the production Native Messaging channel.
const captureEndpoint = "http://127.0.0.1:53921/event";
const heartbeatEndpoint = "http://127.0.0.1:53921/heartbeat";
const targetEndpoint = "http://127.0.0.1:53921/target";
async function reportActiveTarget(token, url, title) {
  await fetch(targetEndpoint, { method: "POST", headers: {
    "Content-Type": "application/json", "X-GuideMe-Capture-Token": token
  }, body: JSON.stringify({ url, title }) });
}
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


// Auto-connect independently of the extension popup. Chrome MV3 workers are
// event-driven, so an alarm also discovers an Instructor started after Chrome.
const autoConnectAlarm = "guideme-instructor-connect";
let connecting = false;

async function ensureConnection(preferredTabId = null, forceInject = false) {
  if (connecting) return;
  connecting = true;
  try {
    const { instructorPairing, instructorAutoDisabled } =
      await chrome.storage.session.get(["instructorPairing", "instructorAutoDisabled"]);
    if (instructorAutoDisabled) return;

    const tab = preferredTabId === null
      ? (await chrome.tabs.query({ active: true, lastFocusedWindow: true }))[0]
      : await chrome.tabs.get(preferredTabId);
    if (!tab?.id || !/^https?:/.test(tab.url || "")) return;

    let pairing = instructorPairing;
    if (pairing && !(await heartbeat(pairing.token))) {
      await chrome.storage.session.remove("instructorPairing");
      pairing = null;
    }
    if (!pairing) {
      await pairAutomatically(tab.id);
      pairing = (await chrome.storage.session.get("instructorPairing")).instructorPairing;
      forceInject = true;
    }
    // The desktop heartbeat proves transport availability, not that the active
    // website has capture listeners. Bind the selected tab and inject there.
    await reportActiveTarget(pairing.token, tab.url, tab.title || "");
    if (pairing.tabId !== tab.id || forceInject) {
      await chrome.scripting.executeScript({
        target: { tabId: tab.id, allFrames: true },
        files: ["content-capture.js"]
      });
      await chrome.storage.session.set({
        instructorPairing: { token: pairing.token, tabId: tab.id }
      });
    }
  } catch {
    // Instructor may be unavailable or this tab may have navigated meanwhile.
  } finally {
    connecting = false;
  }
}

function initializeAutoConnect() {
  chrome.alarms.create(autoConnectAlarm, { periodInMinutes: 0.5 });
  void ensureConnection();
}

async function selectInstructorTarget(tab) {
  if (!tab?.id || !Number.isInteger(tab.windowId) || !/^https?:/.test(tab.url || ""))
    throw new Error("יש לפתוח לשונית אתר פעילה.");
  let { instructorPairing } = await chrome.storage.session.get("instructorPairing");
  if (!instructorPairing || !(await heartbeat(instructorPairing.token))) {
    await pairAutomatically(tab.id);
    instructorPairing = (await chrome.storage.session.get("instructorPairing")).instructorPairing;
  }
  const response = await fetch("http://127.0.0.1:53921/select-target", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-GuideMe-Capture-Token": instructorPairing.token
    },
    body: JSON.stringify({
      url: tab.url, title: tab.title || "", tabId: tab.id, windowId: tab.windowId
    })
  });
  if (!response.ok) throw new Error("Instructor rejected target: " + response.status);
  return { ok: true, url: tab.url };
}

chrome.commands.onCommand.addListener(async command => {
  if (command !== "select-instructor-target") return;
  try {
    const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    await selectInstructorTarget(tab);
  } catch (error) {
    console.warn("GuideMe target selection failed", error);
  }
});

chrome.runtime.onInstalled.addListener(initializeAutoConnect);
chrome.runtime.onStartup.addListener(initializeAutoConnect);
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === autoConnectAlarm) void ensureConnection();
});
chrome.windows.onFocusChanged.addListener(windowId => {
  if (windowId !== chrome.windows.WINDOW_ID_NONE) void ensureConnection();
});
chrome.tabs.onActivated.addListener(({ tabId }) => {
  void ensureConnection(tabId);
});
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" && tab.active && /^https?:/.test(tab.url || "")) {
    void ensureConnection(tabId, true);
  }
});
chrome.tabs.onRemoved.addListener(async tabId => {
  const { instructorPairing } = await chrome.storage.session.get("instructorPairing");
  if (instructorPairing?.tabId === tabId)
    await chrome.storage.session.remove("instructorPairing");
});
// MV3 service workers can restart between events.
initializeAutoConnect();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "guideme-instructor-select-target") {
    chrome.tabs.query({ active: true, currentWindow: true })
      .then(([tab]) => selectInstructorTarget(tab))
      .then(sendResponse)
      .catch(error => sendResponse({ ok: false, error: String(error.message || error) }));
    return true;
  }
  if (message?.type === "guideme-instructor-auto-pair") {
    if (!Number.isInteger(message.tabId)) { sendResponse({ ok: false, error: "Invalid tab" }); return false; }
    chrome.storage.session.remove("instructorAutoDisabled").then(() => pairAutomatically(message.tabId)).then(sendResponse).catch(error => sendResponse({ ok: false, error: String(error.message || error) }));
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
    chrome.storage.session.set({ instructorAutoDisabled: true })
      .then(() => chrome.storage.session.remove("instructorPairing"))
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: String(error) }));
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
