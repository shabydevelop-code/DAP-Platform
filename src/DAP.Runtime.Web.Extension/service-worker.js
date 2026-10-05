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
    const requestedFrameId = message.frameId;
    if (tabId == null) {
      const tabs = await chrome.tabs.query({active:true, lastFocusedWindow:true});
      if (tabs.length !== 1 || tabs[0].id == null)
        throw new Error("DAP adapter could not resolve exactly one active browser tab.");
      tabId = tabs[0].id;
    }

    const payload = { type: "dap-adapter-command", requestId: message.requestId, command: message.command };
    if (requestedFrameId != null) {
      const response = await chrome.tabs.sendMessage(tabId, payload, {frameId:requestedFrameId});
      postAdapterResponse(connect(), message.requestId, response, tabId, requestedFrameId);
      return;
    }

    // No frame was specified by DAP. Ask every injected frame and aggregate
    // target-resolution facts. This is required because TestCRM keeps its
    // actionable DOM inside iframes; frame 0 alone is not a valid default.
    const frames = await chrome.webNavigation.getAllFrames({tabId});
    const replies = [];
    for (const frame of frames) {
      try {
        const response = await chrome.tabs.sendMessage(tabId, payload, {frameId:frame.frameId});
        if (response?.ok) replies.push({frameId:frame.frameId,response});
      } catch {}
    }
    if (!replies.length)
      throw new Error("DAP Web Runtime content script is not available in any frame of the active tab.");

    if (message.command?.type === "resolveTarget") {
      let count = 0;
      for (const reply of replies) count += Number(reply.response?.result?.count || 0);
      const status = count === 0 ? "notFound" : count === 1 ? "resolved" : "ambiguous";
      postAdapterResponse(connect(), message.requestId, {ok:true,result:{status,count}}, tabId, null);
      return;
    }

    postAdapterResponse(connect(), message.requestId, replies[0].response, tabId, replies[0].frameId);
  } catch (error) {
    postAdapterResponse(connect(), message.requestId, { ok:false, error:String(error?.message || error) }, message.tabId ?? null, message.frameId ?? 0);
  }
});
