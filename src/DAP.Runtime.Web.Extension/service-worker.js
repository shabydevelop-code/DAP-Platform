const HOST = "com.dap.web_runtime";
const identifiedFrames = new Map();
let port = null;

function connect() {
  if (port) return port;

  const connected = chrome.runtime.connectNative(HOST);
  port = connected;

  connected.onMessage.addListener(handleNativeMessage);
  connected.onDisconnect.addListener(() => {
    if (port === connected) port = null;
  });

  return connected;
}

function waitForNativeResponse(nativePort, requestId, timeoutMs = 5000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      nativePort.onMessage.removeListener(onMessage);
      reject(new Error("DAP native response timed out."));
    }, timeoutMs);

    const onMessage = message => {
      if (message?.requestId !== requestId) return;
      clearTimeout(timer);
      nativePort.onMessage.removeListener(onMessage);
      resolve(message);
    };

    nativePort.onMessage.addListener(onMessage);
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "dap-frame-identified" && message.token) {
    identifiedFrames.set(message.token, {
      tabId: sender.tab?.id ?? null,
      frameId: sender.frameId ?? 0
    });
    sendResponse({ ok: true });
    return false;
  }

  if (message?.type === "dap-adapter-event") {
    const nativePort = connect();
    const requestId = crypto.randomUUID();

    waitForNativeResponse(nativePort, requestId)
      .then(response => sendResponse(response))
      .catch(error => sendResponse({ ok: false, error: String(error?.message || error) }));

    nativePort.postMessage({
      type: "adapterEvent",
      requestId,
      tabId: sender.tab?.id ?? null,
      frameId: sender.frameId ?? 0,
      payload: message.payload ?? null
    });

    return true;
  }

  if (message?.type === "dap-native") {
    const nativePort = connect();
    const requestId = message.requestId || crypto.randomUUID();

    waitForNativeResponse(nativePort, requestId)
      .then(response => sendResponse(response))
      .catch(error => sendResponse({ ok: false, error: String(error?.message || error) }));

    nativePort.postMessage({ ...message.payload, requestId });
    return true;
  }
});

function postAdapterResponse(requestId, response, tabId, frameId) {
  connect().postMessage({
    type: "adapterResponse",
    requestId,
    tabId,
    frameId,
    response
  });
}

function isMissingReceiverError(error) {
  return String(error?.message || "").includes("Receiving end does not exist");
}

async function ensureContentScript(tabId, frameId) {
  try {
    const ready = await chrome.tabs.sendMessage(
      tabId,
      { type: "dap-adapter-command", command: { type: "ping" } },
      { frameId }
    );
    if (ready?.ok) return;
  } catch (error) {
    if (!isMissingReceiverError(error)) throw error;
  }

  await chrome.scripting.executeScript({
    target: { tabId, frameIds: [frameId] },
    files: ["content-runtime.js"]
  });
}

async function sendToFrame(tabId, frameId, requestId, command) {
  const payload = { type: "dap-adapter-command", requestId, command };

  try {
    return await chrome.tabs.sendMessage(tabId, payload, { frameId });
  } catch (error) {
    if (!isMissingReceiverError(error)) throw error;

    await ensureContentScript(tabId, frameId);
    return chrome.tabs.sendMessage(tabId, payload, { frameId });
  }
}

async function resolveTargetTab() {
  const tabs = await chrome.tabs.query({
    url: ["http://localhost/*", "https://localhost/*"]
  });

  const candidates = tabs.filter(tab => tab.id != null);
  if (candidates.length !== 1) {
    const details = candidates
      .map(tab => tab.id + ":" + (tab.url || "<no-url>"))
      .join(", ");
    throw new Error(
      "DAP adapter expected exactly one eligible localhost application tab but found " +
      candidates.length + ". tabs=[" + details + "]"
    );
  }

  return candidates[0].id;
}

async function resolveFramePath(tabId, framePath, requestId) {
  let frameId = 0;
  let offsetX = 0;
  let offsetY = 0;

  for (const locator of framePath || []) {
    const probe = await sendToFrame(
      tabId,
      frameId,
      requestId + "-frame",
      { type: "resolveFrameChild", locator }
    );

    if (!probe?.ok || probe.result?.status !== "resolved") {
      return {
        ok: false,
        response: probe || { ok: true, result: { status: "notFound", count: 0 } },
        frameId
      };
    }

    const frameRect = probe.result.rect;
    if (frameRect) {
      offsetX += Number(frameRect.x || 0);
      offsetY += Number(frameRect.y || 0);
    }

    const token = probe.result.frameToken;
    let identified = identifiedFrames.get(token);

    for (let i = 0; !identified && i < 20; i++) {
      await new Promise(resolve => setTimeout(resolve, 25));
      identified = identifiedFrames.get(token);
    }

    identifiedFrames.delete(token);

    if (!identified || identified.tabId !== tabId) {
      throw new Error("DAP could not map the resolved iframe element to a browser frame.");
    }

    frameId = identified.frameId;
  }

  return { ok: true, frameId, offsetX, offsetY };
}

async function hideEveryFrame(tabId, requestId) {
  const frames = await chrome.webNavigation.getAllFrames({ tabId }) || [];

  for (const frame of frames) {
    try {
      await sendToFrame(
        tabId,
        frame.frameId,
        requestId + "-hide-" + frame.frameId,
        { type: "hideBubble" }
      );
    } catch {
      // A frame can disappear while the page is rebuilding. Hide is best-effort
      // across retired frames, matching Playwright cleanup semantics.
    }
  }
}

async function handleNativeMessage(message) {
  if (message?.type !== "adapterCommand" || !message.requestId) return;

  const requestId = message.requestId;

  try {
    const tabId = message.tabId ?? await resolveTargetTab();
    const command = { ...(message.command || {}) };

    if (command.type === "hideBubble") {
      await hideEveryFrame(tabId, requestId);
      postAdapterResponse(requestId, { ok: true, result: { status: "hidden" } }, tabId, null);
      return;
    }

    // Playwright baseline semantics:
    // no FrameContext means page.MainFrame; a non-empty FrameContext is resolved
    // explicitly from the top frame. Do not broadcast target commands to every frame.
    let frameId = 0;
    let frameOffsetX = 0;
    let frameOffsetY = 0;
    if (Array.isArray(command.framePath) && command.framePath.length > 0) {
      const frameResolution = await resolveFramePath(tabId, command.framePath, requestId);
      if (!frameResolution.ok) {
        postAdapterResponse(requestId, frameResolution.response, tabId, frameResolution.frameId);
        return;
      }
      frameId = frameResolution.frameId;
      frameOffsetX = frameResolution.offsetX || 0;
      frameOffsetY = frameResolution.offsetY || 0;
    }

    delete command.framePath;

    const response = await sendToFrame(tabId, frameId, requestId, command);

    if (
      command.type === "ensureBubble" &&
      response?.ok &&
      response.result?.needsTopLevel === true
    ) {
      const localRect = response.result.rect;
      const targetRect = response.result.topRect || (localRect ? {
        x: frameOffsetX + Number(localRect.x || 0),
        y: frameOffsetY + Number(localRect.y || 0),
        width: Number(localRect.width || 0),
        height: Number(localRect.height || 0)
      } : null);

      if (!targetRect) {
        postAdapterResponse(
          requestId,
          { ok:false, error:"DAP could not derive top-level target geometry for bubble proxy." },
          tabId,
          frameId
        );
        return;
      }

      const proxy = await sendToFrame(
        tabId,
        0,
        requestId + "-proxy",
        {
          type: "showBubbleProxy",
          step: command.step,
          stepNumber: command.stepNumber,
          totalSteps: command.totalSteps,
          progressText: command.progressText,
          dragText: command.dragText,
          direction: command.direction,
          targetRect
        }
      );

      if (!proxy?.ok) {
        postAdapterResponse(
          requestId,
          proxy || { ok: false, error: "Top-level bubble proxy failed." },
          tabId,
          0
        );
        return;
      }
    }

    postAdapterResponse(requestId, response, tabId, frameId);
  } catch (error) {
    postAdapterResponse(
      requestId,
      { ok: false, error: String(error?.message || error) },
      message.tabId ?? null,
      message.frameId ?? 0
    );
  }
}

connect();
