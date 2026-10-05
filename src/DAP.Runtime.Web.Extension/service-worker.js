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
      // Never bind DAP to whichever browser tab happens to be active. During
      // this POC the extension is scoped to localhost, so select the single
      // eligible application tab deterministically.
      const tabs = await chrome.tabs.query({url:["http://localhost/*","https://localhost/*"]});
      const candidates = tabs.filter(tab => tab.id != null);
      if (candidates.length !== 1) {
        const details = candidates.map(tab => tab.id + ":" + (tab.url || "<no-url>")).join(", ");
        throw new Error("DAP adapter expected exactly one eligible localhost application tab but found " + candidates.length + ". tabs=[" + details + "]");
      }
      tabId = candidates[0].id;
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
    if (!replies.length) {
      // Declarative content-script injection can be unavailable after extension
      // reloads or host-access changes. Recover explicitly, then retry once.
      await chrome.scripting.executeScript({
        target: {tabId, allFrames:true},
        files: ["content-runtime.js"]
      });
      const retryErrors = [];
      for (const frame of frames) {
        try {
          const response = await chrome.tabs.sendMessage(tabId, payload, {frameId:frame.frameId});
          if (response?.ok) replies.push({frameId:frame.frameId,response});
          else retryErrors.push(frame.frameId + ": response=" + JSON.stringify(response));
        } catch (error) {
          retryErrors.push(frame.frameId + ": " + String(error?.message || error));
        }
      }
      if (!replies.length) {
        const details = frames.map(f => f.frameId + ":" + (f.url || "<no-url>")).join(", ");
        throw new Error("DAP Web Runtime content script is not available after explicit injection. tabId=" + tabId + "; frames=[" + details + "]; sendErrors=[" + retryErrors.join(" | ") + "]");
      }
    }

    if (message.command?.type === "resolveTarget") {
      let count = 0;
      for (const reply of replies) count += Number(reply.response?.result?.count || 0);
      const status = count === 0 ? "notFound" : count === 1 ? "resolved" : "ambiguous";
      postAdapterResponse(connect(), message.requestId, {ok:true,result:{status,count}}, tabId, null);
      return;
    }

    if (message.command?.type === "isContextActive") {
      const active = replies.some(reply => reply.response?.result?.active === true);
      postAdapterResponse(connect(), message.requestId, {ok:true,result:{active}}, tabId, null);
      return;
    }

    if (message.command?.type === "armValidation") {
      const resolved = replies.filter(r => r.response?.result?.status === "resolved");
      const ambiguous = replies.reduce((n, r) => n + (r.response?.result?.status === "ambiguous" ? Number(r.response.result.count || 0) : 0), 0);
      const count = resolved.length + ambiguous;
      const status = count === 0 ? "notFound" : count === 1 ? "resolved" : "ambiguous";
      if (status === "resolved") {
        for (const reply of replies) {
          if (reply.response?.result?.status !== "resolved") continue;
          postAdapterResponse(connect(), message.requestId, {ok:true,result:{status,count:1}}, tabId, reply.frameId);
          return;
        }
      }
      postAdapterResponse(connect(), message.requestId, {ok:true,result:{status,count}}, tabId, null);
      return;
    }

    if (message.command?.type === "readTargetValue") {
      const matches = replies.filter(r => r.response?.result?.status === "resolved");
      const ambiguous = replies.reduce((n, r) => n + (r.response?.result?.status === "ambiguous" ? Number(r.response.result.count || 0) : 0), 0);
      const count = matches.length + ambiguous;
      if (count === 0) postAdapterResponse(connect(), message.requestId, {ok:true,result:{status:"notFound",count:0,value:null}}, tabId, null);
      else if (count !== 1) postAdapterResponse(connect(), message.requestId, {ok:true,result:{status:"ambiguous",count,value:null}}, tabId, null);
      else postAdapterResponse(connect(), message.requestId, matches[0].response, tabId, matches[0].frameId);
      return;
    }

    if (message.command?.type === "waitForDomQuiet") {
      const stable = replies.every(reply => reply.response?.result?.stable === true);
      postAdapterResponse(connect(), message.requestId, {ok:true,result:{stable}}, tabId, null);
      return;
    }

    postAdapterResponse(connect(), message.requestId, replies[0].response, tabId, replies[0].frameId);
  } catch (error) {
    postAdapterResponse(connect(), message.requestId, { ok:false, error:String(error?.message || error) }, message.tabId ?? null, message.frameId ?? 0);
  }
});
