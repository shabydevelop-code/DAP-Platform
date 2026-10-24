// The Instructor extension has its own Native Messaging host; no HTTP pairing.
// It must never use the Learner host or exchange guide execution messages.
const nativeHost = "com.guideme.instructor";
const alarmName = "guideme-instructor-sync";
let port = null;
let selectedTabId = null;
let pickerArmedTabId = null;
let pickerArmedSession = null;
let pickerFrames = [];
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
      pickerArmedTabId = null;
      pickerArmedSession = null;
      pickerFrames = [];
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
  return chrome.scripting.executeScript({
    target:{tabId,allFrames:true},files:["content-capture.js"]
  });
}
async function stopPicker(tabId) {
  const frames = pickerFrames;
  pickerFrames = [];
  pickerArmedTabId = null;
  pickerArmedSession = null;
  await Promise.all(frames.map(frameId => chrome.tabs.sendMessage(tabId,
    {type:"guideme-instructor-pick-stop"},{frameId}).catch(()=>{})));
}
async function reportPickerError(tabId, error) {
  const reason = String(error?.message || error || "Unknown Chrome error").slice(0, 320);
  console.warn("GuideMe Instructor picker:", reason);
  await request("picker-error", {tabId, reason}).catch(() => {});
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
    selectedTabId = next;
    const pickTabId = result.pickTabId ?? null;
    const pickerSession = result.pickerSession ?? null;
    // A navigation can replace the document (including a nested iframe) while
    // the Instructor capture session remains active. Detect missing content
    // listeners and re-arm without cancelling the user's pending selection.
    if (pickTabId !== null && pickerArmedTabId === pickTabId &&
        pickerArmedSession === pickerSession) {
      try {
        const checks = await chrome.scripting.executeScript({
          target:{tabId:pickTabId,allFrames:true},
          func:() => globalThis.__guideMeCaptureInstalled === true &&
            globalThis.__guideMePickerActive === true
        });
        if (!checks.length || checks.some(frame => frame.result !== true))
          pickerArmedSession = null;
      } catch {
        // A page may be in the middle of navigation; retry on next sync.
        pickerArmedSession = null;
      }
    }
    if (pickTabId !== null && (pickerArmedTabId !== pickTabId || pickerArmedSession !== pickerSession)) {
      try {
        if (pickerArmedTabId !== null) await stopPicker(pickerArmedTabId);
        const injected = await inject(pickTabId);
        const frames = [...new Set(injected.map(item=>item.frameId))];
        const nonce = crypto.randomUUID();
        const results = await Promise.all(frames.map(async frameId => {
          try {
            const reply = await chrome.tabs.sendMessage(pickTabId,
              {type:"guideme-instructor-pick-start",nonce},{frameId});
            return reply?.armed === true ? frameId : null;
          } catch { return null; }
        }));
        const armed = results.filter(id=>id!==null);
        if (!armed.includes(0)) throw new Error("Inspector could not activate in top frame");
        pickerFrames = armed;
        const ack = await request("picker-armed",{tabId:pickTabId});
        if (ack?.accepted === true) {
          pickerArmedTabId = pickTabId;
          pickerArmedSession = pickerSession;
        }
      } catch (error) { await reportPickerError(pickTabId,error); }
    }
    if (pickTabId === null && pickerArmedTabId !== null)
      await stopPicker(pickerArmedTabId);
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
    if (selectedTabId === id) { pickerArmedTabId = null; pickerArmedSession = null; pickerFrames = []; }
    void sync();
  }
});
chrome.windows.onFocusChanged.addListener(() => void sync());
chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type === "guideme-instructor-pick-error" && sender.tab?.id === selectedTabId) {
    void request("picker-error", {tabId:sender.tab.id,reason:String(message.reason || "בחירת הרכיב נכשלה").slice(0,320)}).catch(()=>{});
    return false;
  }
  if (message?.type === 'guideme-instructor-pick-cancelled' && sender.tab?.id === selectedTabId) {
    // Escape is received in one frame only. Disarm every injected frame,
    // otherwise sibling/parent frames keep their blue outlines and hover polling.
    const cancelledTabId = sender.tab.id;
    void stopPicker(cancelledTabId)
      .then(() => request('picker-cancelled', {tabId:cancelledTabId}))
      .catch(error => console.warn('Instructor picker cancel:', error.message));
    return false;
  }
  if (message?.type === "guideme-instructor-picked" && sender.tab?.id === selectedTabId && sender.frameId === 0) {
    void request("picked", { payload: { tabId: sender.tab.id, event: message.event } }).then(() => stopPicker(sender.tab.id)).catch(() => {});
    return false;
  }
  if (message?.type !== "guideme-instructor-event" || sender.tab?.id !== selectedTabId) return false;
  const event = message.event;
  if (!event || !["click", "change", "blur"].includes(event.kind)) return false;
  void request("event", { payload: { tabId: sender.tab.id, event } }).catch(() => {});
  return false;
});
chrome.commands.onCommand.addListener(command => {
  if (command !== "pick-highlighted" || pickerArmedTabId === null) return;
  const tabId = pickerArmedTabId;
  void Promise.all(pickerFrames.map(frameId => chrome.tabs.sendMessage(tabId,
    {type:"guideme-instructor-pick-confirm"},{frameId}).catch(()=>{})));
});
chrome.alarms.create(alarmName, { periodInMinutes: 0.5 });
connect();
void sync();
setInterval(() => void sync(), 3000);
