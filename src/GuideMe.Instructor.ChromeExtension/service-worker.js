// The Instructor extension has its own Native Messaging host; no HTTP pairing.
// It must never use the Learner host or exchange guide execution messages.
const nativeHost = "com.guideme.instructor";
const alarmName = "guideme-instructor-sync";
let port = null;
let selectedTabId = null;
let pickerArmedTabId = null;
let pending = new Map();
let serial = 0;
let connecting = false;

function request(type, fields = {}) {
  if (!port) return Promise.reject(new Error("Instructor Native Host disconnected"));
  const requestId = String(++serial);
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pending.delete(requestId);
      reject(new Error("Instructor Native Host timeout"));
    }, 4500);
    pending.set(requestId, { resolve, reject, timeout });
    try { port.postMessage({ requestId, type, ...fields }); }
    catch (error) {
      clearTimeout(timeout);
      pending.delete(requestId);
      reject(error);
    }
  });
}
function connect() {
  if (port || connecting) return;
  connecting = true;
  try {
    const candidate = chrome.runtime.connectNative(nativeHost);
    port = candidate;
    candidate.onMessage.addListener(message => {
      const entry = pending.get(message?.requestId);
      if (!entry) return;
      clearTimeout(entry.timeout);
      pending.delete(message.requestId);
      if (message.ok) entry.resolve(message.result);
      else entry.reject(new Error(message.error || "Native Host error"));
    });
    candidate.onDisconnect.addListener(() => {
      if (port !== candidate) return;
      port = null;
      selectedTabId = null;
      for (const entry of pending.values()) {
        clearTimeout(entry.timeout);
        entry.reject(new Error(chrome.runtime.lastError?.message || "Native Host closed"));
      }
      pending.clear();
    });
  } catch (error) {
    port = null;
    console.warn("Instructor Native Messaging:", error);
  } finally { connecting = false; }
}
function allowedTab(tab) {
  return Number.isInteger(tab?.id) && /^https?:\/\//i.test(tab.url || "") &&
    !(tab.url || "").startsWith("http://127.0.0.1:53922/");
}
async function listTabs() {
  return (await chrome.tabs.query({})).filter(allowedTab).slice(0, 100).map(t => ({
    tabId: t.id, url: t.url, title: String(t.title || "").slice(0, 200)
  }));
}
async function inject(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId, allFrames: true }, files: ["content-capture.js"]
    });
  } catch (error) { console.warn("Instructor injection failed:", error); }
}
let syncing = false;
async function sync() {
  if (syncing) return;
  syncing = true;
  try {
    connect();
    if (!port) return;
    const result = await request("heartbeat", { tabs: await listTabs() });
    const next = result.selectedTabId ?? null;
    if (next !== null && next !== selectedTabId) await inject(next);
    selectedTabId = next;
    const pickTabId = result.pickTabId ?? null;
    if (pickTabId !== null && pickerArmedTabId !== pickTabId) {
      await inject(pickTabId);
      const armed = await chrome.tabs.sendMessage(pickTabId, { type: "guideme-instructor-pick-start" }, {frameId:0}).then(() => true, () => false);
      if (armed) {
        await request("picker-armed", { tabId: pickTabId });
        pickerArmedTabId = pickTabId;
      }
    }
    if (pickTabId === null && pickerArmedTabId !== null)
      await chrome.tabs.sendMessage(pickerArmedTabId, { type: "guideme-instructor-pick-stop" }, {frameId:0}).catch(() => {});
    if (pickTabId === null) pickerArmedTabId = null;
  } catch (error) {
    console.warn("Instructor sync:", error.message);
  } finally { syncing = false; }
}
chrome.runtime.onInstalled.addListener(() => { chrome.alarms.create(alarmName, { periodInMinutes: 0.5 }); void sync(); });
chrome.runtime.onStartup.addListener(() => { chrome.alarms.create(alarmName, { periodInMinutes: 0.5 }); void sync(); });
chrome.alarms.onAlarm.addListener(a => { if (a.name === alarmName) void sync(); });
chrome.tabs.onCreated.addListener(() => void sync());
chrome.tabs.onActivated.addListener(() => void sync());
chrome.tabs.onRemoved.addListener(id => { if (id === selectedTabId) selectedTabId = null; void sync(); });
chrome.tabs.onUpdated.addListener((id, change) => {
  if (change.status === "complete") {
    if (selectedTabId === id) void inject(id);
    void sync();
  }
});
chrome.windows.onFocusChanged.addListener(() => void sync());
chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type === "guideme-instructor-picked" && sender.tab?.id === selectedTabId) {
    void request("picked", { payload: { tabId: sender.tab.id, event: message.event } }).catch(() => {});
    return false;
  }
  if (message?.type !== "guideme-instructor-event" || sender.tab?.id !== selectedTabId) return false;
  const event = message.event;
  if (!event || !["click", "change", "blur"].includes(event.kind)) return false;
  void request("event", { payload: { tabId: sender.tab.id, event } }).catch(() => {});
  return false;
});
chrome.alarms.create(alarmName, { periodInMinutes: 0.5 });
connect();
void sync();
setInterval(() => void sync(), 3000);
