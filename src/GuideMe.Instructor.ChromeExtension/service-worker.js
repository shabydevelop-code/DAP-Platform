// Headless Instructor-only transport. No popup, shortcut or manual target selection.
// Authoring permissions are opened in the Web Instructor for a short pairing window.
const base = "http://127.0.0.1:53922/capture";
const alarmName = "guideme-instructor-sync";
let syncing = false;
let selectedTabId = null;

async function post(path, token, value) {
  const response = await fetch(base + path, {
    method: "POST",
    headers: {
      ...(value === undefined ? {} : { "Content-Type": "application/json" }),
      ...(token ? { "X-GuideMe-Capture-Token": token } : {})
    },
    ...(value === undefined ? {} : { body: JSON.stringify(value) })
  });
  if (!response.ok) throw new Error("Instructor endpoint " + path + ": " + response.status);
  return response.status === 204 ? null : response.json();
}
async function getPairing() {
  const result = await chrome.storage.session.get("instructorPairing");
  return result.instructorPairing?.token ?? null;
}
function allowedTab(tab) {
  return Number.isInteger(tab?.id) &&
    /^https?:\/\//i.test(tab.url || "") &&
    !(tab.url || "").startsWith("http://127.0.0.1:53922/");
}
async function listTabs() {
  const tabs = await chrome.tabs.query({});
  return tabs.filter(allowedTab).slice(0, 100).map(t => ({
    tabId: t.id, url: t.url, title: String(t.title || "").slice(0, 200)
  }));
}
async function inject(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId, allFrames: true },
      files: ["content-capture.js"]
    });
  } catch (error) {
    console.warn("GuideMe Instructor could not attach to selected tab", error);
  }
}
async function sync() {
  if (syncing) return;
  syncing = true;
  try {
    let token = await getPairing();
    if (!token) {
      try {
        const paired = await post("/pair", null);
        if (!/^[a-f0-9]{32}$/.test(paired.token)) return;
        token = paired.token;
        await chrome.storage.session.set({ instructorPairing: { token } });
      } catch { return; } // Pairing window is closed; do not access websites.
    }
    let result;
    try {
      result = await post("/heartbeat", token, await listTabs());
    } catch {
      await chrome.storage.session.remove("instructorPairing");
      selectedTabId = null;
      return;
    }
    const nextTabId = result.selectedTabId ?? null;
    if (nextTabId !== selectedTabId && nextTabId !== null)
      await inject(nextTabId);
    selectedTabId = nextTabId;
  } finally {
    syncing = false;
  }
}
chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(alarmName, { periodInMinutes: 0.5 });
  void sync();
});
chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create(alarmName, { periodInMinutes: 0.5 });
  void sync();
});
chrome.alarms.onAlarm.addListener(a => { if (a.name === alarmName) void sync(); });
chrome.tabs.onCreated.addListener(() => void sync());
chrome.tabs.onActivated.addListener(() => void sync());
chrome.tabs.onRemoved.addListener(id => {
  if (selectedTabId === id) selectedTabId = null;
  void sync();
});
chrome.tabs.onUpdated.addListener((id, change) => {
  if (change.status === "complete") {
    if (selectedTabId === id) void inject(id);
    void sync();
  }
});
chrome.windows.onFocusChanged.addListener(() => void sync());
chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type !== "guideme-instructor-event" || sender.tab?.id !== selectedTabId) return false;
  const event = message.event;
  if (!event || !["click", "change", "blur"].includes(event.kind)) return false;
  void getPairing().then(token => {
    if (token) return post("/event", token, { tabId: sender.tab.id, event });
  }).catch(() => {});
  return false;
});
chrome.alarms.create(alarmName, { periodInMinutes: 0.5 });
void sync();
