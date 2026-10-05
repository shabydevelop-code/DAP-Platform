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
  if (message?.type === "dap-runtime-ready") {
    connect();
    sendResponse({ ok: true });
    return false;
  }

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
  } catch {
    // A page can retain an isolated world from the previous unpacked-extension
    // generation. Treat any failed readiness probe as a stale/missing endpoint
    // and install the current runtime into this exact browser frame.
  }

  await chrome.scripting.executeScript({
    target: { tabId, frameIds: [frameId] },
    files: ["content-runtime.js"]
  });

  const ready = await chrome.tabs.sendMessage(
    tabId,
    { type: "dap-adapter-command", command: { type: "ping" } },
    { frameId }
  );
  if (!ready?.ok)
    throw new Error("DAP content runtime did not become ready after injection.");
}

async function ensureTabFramesReady(tabId) {
  const frames = await chrome.webNavigation.getAllFrames({ tabId }) || [];
  for (const frame of frames) {
    try {
      await ensureContentScript(tabId, frame.frameId);
    } catch (error) {
      // A frame can disappear while TestCRM/server applications rebuild their
      // document tree. It will be re-discovered by the next command.
      const current = await chrome.webNavigation.getFrame({ tabId, frameId: frame.frameId })
        .catch(() => null);
      if (current) throw error;
    }
  }
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
  // Frame-element -> browser-frame identification uses a short postMessage
  // handshake. Ensure every currently live frame runs the current extension
  // generation first; otherwise a child left behind by an extension reload can
  // answer the DOM handshake but be unable to report its chrome frameId.
  await ensureTabFramesReady(tabId);

  let frameId = 0;
  let offsetX = 0;
  let offsetY = 0;
  let currentFrameRect = null;

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
      currentFrameRect = {
        x: offsetX,
        y: offsetY,
        width: Number(frameRect.width || 0),
        height: Number(frameRect.height || 0)
      };
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

  return { ok: true, frameId, offsetX, offsetY, frameRect: currentFrameRect };
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
      // across retired frames, matching established Web cleanup semantics.
    }
  }
}

function sessionFromUrl(url) {
  try { return new URL(url).searchParams.get("dap-e2e-session"); }
  catch { return null; }
}

async function resolveTestTab(sessionId) {
  const tabs = await chrome.tabs.query({
    url: ["http://localhost/*", "https://localhost/*"]
  });
  const matches = tabs.filter(tab =>
    tab.id != null &&
    sessionFromUrl(tab.url || "") === sessionId
  );
  if (matches.length !== 1) {
    const details = matches.map(tab => tab.id + ":" + (tab.url || "<no-url>")).join(", ");
    throw new Error(
      "DAP test driver expected exactly one session tab for '" + sessionId +
      "' but found " + matches.length + ". tabs=[" + details + "]"
    );
  }
  return matches[0].id;
}

async function resolveTestFrame(tabId, frameName) {
  if (!frameName) return 0;
  const frames = await chrome.webNavigation.getAllFrames({ tabId }) || [];
  for (const frame of frames) {
    try {
      const info = await sendToFrame(
        tabId,
        frame.frameId,
        "test-frame-info-" + frame.frameId,
        { type: "testFrameInfo" }
      );
      if (info?.ok && info.result?.name === frameName)
        return frame.frameId;
    } catch {}
  }
  throw new Error("DAP test driver could not find frame '" + frameName + "'.");
}

async function testFrames(tabId) {
  const frames = await chrome.webNavigation.getAllFrames({ tabId }) || [];
  const result = [];
  for (const frame of frames) {
    try {
      const info = await sendToFrame(
        tabId,
        frame.frameId,
        "test-frame-list-" + frame.frameId,
        { type: "testFrameInfo" }
      );
      if (info?.ok) result.push({
        frameId: frame.frameId,
        name: info.result?.name || "",
        url: info.result?.url || frame.url || ""
      });
    } catch {}
  }
  return result;
}

async function testFrameOffset(tabId, frameName) {
  if (!frameName) return { x: 0, y: 0 };
  const top = await sendToFrame(
    tabId,
    0,
    "test-frame-offset-" + frameName,
    {
      type: "testLocator",
      path: [{
        selector: frameName === "dap-content"
          ? "#content-frame"
          : "iframe[name='" + frameName.replaceAll("'", "\\'") + "']",
        index: 0
      }],
      op: "box"
    }
  );
  const box = top?.result?.box;
  return box ? { x: Number(box.x || 0), y: Number(box.y || 0) } : { x: 0, y: 0 };
}

async function handleTestDriverMessage(message) {
  const requestId = message.requestId;
  const sessionId = String(message.sessionId || "");
  if (!requestId || !sessionId) return;

  try {
    const tabId = await resolveTestTab(sessionId);
    const command = { ...(message.command || {}) };

    if (command.type === "testPing") {
      const tab = await chrome.tabs.get(tabId);
      connect().postMessage({
        type: "testDriverResponse",
        requestId,
        response: { ok: true, result: { tabId, url: tab.url || "" } }
      });
      return;
    }

    if (command.type === "testNavigate") {
      const url = new URL(String(command.url || "http://localhost/"));
      url.searchParams.set("dap-e2e-session", sessionId);
      await chrome.tabs.update(tabId, { url: url.toString() });
      connect().postMessage({
        type: "testDriverResponse",
        requestId,
        response: { ok: true, result: { status: "navigating" } }
      });
      return;
    }

    if (command.type === "testGetFrames") {
      const frames = await testFrames(tabId);
      connect().postMessage({
        type: "testDriverResponse",
        requestId,
        response: { ok: true, result: { frames } }
      });
      return;
    }

    if (command.type === "testResolveFrame") {
      const framePath = Array.isArray(command.framePath) ? command.framePath : [];
      const resolved = await resolveFramePath(tabId, framePath, requestId + "-test-resolve");
      if (!resolved.ok) {
        connect().postMessage({
          type: "testDriverResponse",
          requestId,
          response: resolved.response
        });
        return;
      }

      const info = await sendToFrame(
        tabId,
        resolved.frameId,
        requestId + "-test-info",
        { type: "testFrameInfo" }
      );

      connect().postMessage({
        type: "testDriverResponse",
        requestId,
        response: {
          ok: true,
          result: {
            frameId: resolved.frameId,
            name: info?.result?.name || "",
            url: info?.result?.url || ""
          }
        }
      });
      return;
    }

    if (command.type === "testCloseTab") {
      await chrome.tabs.remove(tabId);
      connect().postMessage({
        type: "testDriverResponse",
        requestId,
        response: { ok: true, result: { status: "closed" } }
      });
      return;
    }

    const frameName = String(command.frameName || "");
    const frameId = await resolveTestFrame(tabId, frameName);
    delete command.frameName;

    const response = await sendToFrame(tabId, frameId, requestId, command);

    if (
      command.type === "testLocator" &&
      command.op === "box" &&
      response?.ok &&
      response.result?.box &&
      frameName
    ) {
      const offset = await testFrameOffset(tabId, frameName);
      response.result.box.x = Number(response.result.box.x || 0) + offset.x;
      response.result.box.y = Number(response.result.box.y || 0) + offset.y;
    }

    connect().postMessage({
      type: "testDriverResponse",
      requestId,
      response
    });
  } catch (error) {
    connect().postMessage({
      type: "testDriverResponse",
      requestId,
      response: { ok: false, error: String(error?.message || error) }
    });
  }
}

async function handleNativeMessage(message) {
  if (message?.type === "testDriverCommand") {
    await handleTestDriverMessage(message);
    return;
  }
  if (message?.type !== "adapterCommand" || !message.requestId) return;

  const requestId = message.requestId;

  try {
    const tabId = message.tabId ?? (
      message.sessionId
        ? await resolveTestTab(String(message.sessionId))
        : await resolveTargetTab()
    );
    const command = { ...(message.command || {}) };
    const originalFramePath = Array.isArray(command.framePath)
      ? command.framePath.map(locator => ({ ...locator }))
      : [];

    if (command.type === "hideBubble") {
      await hideEveryFrame(tabId, requestId);
      postAdapterResponse(requestId, { ok: true, result: { status: "hidden" } }, tabId, null);
      return;
    }

    // Web baseline semantics:
    // no FrameContext means page.MainFrame; a non-empty FrameContext is resolved
    // explicitly from the top frame. Do not broadcast target commands to every frame.
    let frameId = 0;
    let frameOffsetX = 0;
    let frameOffsetY = 0;
    let resolvedFrameRect = null;
    if (Array.isArray(command.framePath) && command.framePath.length > 0) {
      const frameResolution = await resolveFramePath(tabId, command.framePath, requestId);
      if (!frameResolution.ok) {
        postAdapterResponse(requestId, frameResolution.response, tabId, frameResolution.frameId);
        return;
      }
      frameId = frameResolution.frameId;
      frameOffsetX = frameResolution.offsetX || 0;
      frameOffsetY = frameResolution.offsetY || 0;
      resolvedFrameRect = frameResolution.frameRect || null;
    }

    delete command.framePath;

    const response = await sendToFrame(tabId, frameId, requestId, command);

    if (
      command.type === "ensureBubble" &&
      response?.ok &&
      response.result?.needsTopLevel === true
    ) {
      const localRect = response.result.rect;

      // Reconstruct top-level browser page coordinates from the
      // browser-routed frame path. For a target that spans almost the entire
      // child frame (for example the application header), anchor the proxy to
      // the child frame's visible box rather than to a potentially stale local
      // width reported during an iframe resize. This keeps the proxy visually
      // attached to the actual cross-frame target without changing target
      // resolution or validation semantics.
      const routedRect = localRect ? {
        x: frameOffsetX + Number(localRect.x || 0),
        y: frameOffsetY + Number(localRect.y || 0),
        width: Number(localRect.width || 0),
        height: Number(localRect.height || 0)
      } : null;

      let targetRect = routedRect || response.result.topRect || null;

      if (targetRect && resolvedFrameRect && localRect) {
        const localWidth = Number(localRect.width || 0);
        const localHeight = Number(localRect.height || 0);
        const frameWidth = Number(resolvedFrameRect.width || 0);
        const frameHeight = Number(resolvedFrameRect.height || 0);

        const spansFrameWidth = frameWidth > 0 && localWidth >= frameWidth * 0.9;
        const spansFrameHeight = frameHeight > 0 && localHeight >= frameHeight * 0.9;

        if (spansFrameWidth)
          targetRect = { ...targetRect, x: resolvedFrameRect.x, width: frameWidth };
        if (spansFrameHeight)
          targetRect = { ...targetRect, y: resolvedFrameRect.y, height: frameHeight };
      }

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
          targetRect,
          localTargetRect: localRect || null,
          topFrameLocator: originalFramePath.length === 1 ? originalFramePath[0] : null
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
