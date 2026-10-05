const HOST = "com.dap.web_runtime";
let port;
function connect() {
  if (port) return port;
  port = chrome.runtime.connectNative(HOST);
  port.onDisconnect.addListener(() => { port = null; });
  return port;
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "dap-native") {
    const p = connect();
    const requestId = message.requestId || crypto.randomUUID();
    const listener = response => {
      if (response?.requestId !== requestId) return;
      p.onMessage.removeListener(listener);
      sendResponse(response);
    };
    p.onMessage.addListener(listener);
    p.postMessage({ ...message.payload, requestId });
    return true;
  }

  if (message?.type === "dap-adapter-event") {
    const p = connect();
    p.postMessage({
      type: "adapterEvent",
      tabId: sender.tab?.id ?? null,
      frameId: sender.frameId ?? 0,
      payload: message.payload ?? null
    });
    sendResponse({ ok: true });
    return false;
  }
});

function postAdapterResponse(p, requestId, response, tabId, frameId) {
  p.postMessage({ type: "adapterResponse", requestId, tabId, frameId, response });
}

connect().onMessage.addListener(async message => {
  if (message?.type !== "adapterCommand" || !message.requestId) return;
  try {
    let tabId = message.tabId;
    const frameId = message.frameId ?? 0;
    if (tabId == null) {
      const tabs = await chrome.tabs.query({active:true, lastFocusedWindow:true});
      if (tabs.length !== 1 || tabs[0].id == null)
        throw new Error("DAP adapter could not resolve exactly one active browser tab.");
      tabId = tabs[0].id;
    }
    const response = await chrome.tabs.sendMessage(
      tabId,
      { type: "dap-adapter-command", requestId: message.requestId, command: message.command },
      { frameId });
    postAdapterResponse(connect(), message.requestId, response, tabId, frameId);
  } catch (error) {
    postAdapterResponse(connect(), message.requestId, { ok:false, error:String(error?.message || error) }, message.tabId ?? null, message.frameId ?? 0);
  }
});
