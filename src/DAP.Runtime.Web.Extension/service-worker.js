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
