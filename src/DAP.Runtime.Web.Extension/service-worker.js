const HOST = "com.dap.web_runtime";
const TEST_DRIVER_VERSION = "1.0.1";
const identifiedFrames = new Map();
let port = null;
const productionContextTabs = new Map();
const activatedProductionSessions = new Set();
const closedProductionContexts = new Set();

chrome.tabs.onRemoved.addListener(tabId => {
  for (const [contextKey, retainedTabId] of productionContextTabs) {
    if (retainedTabId === tabId) {
      productionContextTabs.delete(contextKey);
      closedProductionContexts.add(contextKey);
      if (port) {
        port.postMessage({ type: "adapterEvent", payload: { type: "target-tab-closed", applicationContextKey: contextKey } });
      }
    }
  }
});

function connect() {
  if (port) return port;

  const connected = chrome.runtime.connectNative(HOST);
  port = connected;

  connected.onMessage.addListener(handleNativeMessage);
  connected.onDisconnect.addListener(() => {
    // Reading lastError acknowledges Chrome's expected native-messaging
    // disconnect when DAP/the runner exits, preventing an unchecked
    // runtime error warning. Connection cleanup remains identical.
    void chrome.runtime.lastError;

    if (port === connected) {
      port = null;
      productionContextTabs.clear();
      activatedProductionSessions.clear();
      closedProductionContexts.clear();
    }
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

function ensureNativeConnection() {
  try { connect(); } catch {}
}

// MV3 workers can be suspended while DAP is not running. A normal browser
// navigation is therefore also a production transport wake-up signal. This is
// transport lifecycle only; it does not identify the Guide target or advance a
// Step.
chrome.runtime.onStartup.addListener(ensureNativeConnection);
chrome.runtime.onInstalled.addListener(ensureNativeConnection);
chrome.tabs.onUpdated.addListener((_tabId, changeInfo) => {
  const url = changeInfo.url || "";
  if (url.startsWith("http://") || url.startsWith("https://"))
    ensureNativeConnection();
});

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
    // A semantic response such as "inactive" or "notFound" is still ok=true,
    // but it must not cause .NET to bind this DAP run to a browser profile
    // that does not contain the persisted Application Context.
    contextMatched: tabId != null,
    response
  });
}

function isTransientReceiverError(error) {
  const message = String(error?.message || error || "");
  return message.includes("Receiving end does not exist") ||
    message.includes("message channel closed before a response was received") ||
    message.includes("The message port closed before a response was received") ||
    message.includes("No frame with id") ||
    message.includes("Frame with ID") ||
    message.includes("The frame was removed");
}

async function ensureContentScript(tabId, frameId) {
  try {
    const ready = await chrome.tabs.sendMessage(
      tabId,
      { type: "dap-adapter-command", command: { type: "ping" } },
      { frameId }
    );
    // A successful ping from an older injected content script is not readiness.
    // Require the current endpoint generation before routing commands.
    if (ready?.ok && ready.result?.version === "0.4.7") return;
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
  if (!ready?.ok || ready.result?.version !== "0.4.7")
    throw new Error("DAP content runtime is stale after injection; reload the unpacked extension in chrome://extensions.");
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
    if (!isTransientReceiverError(error)) throw error;
  }

  // A SPA/server refresh can retire the document after it received the
  // command but before its asynchronous sendResponse fires. Re-resolve the
  // current browser frame/content runtime once and retry against the live
  // document. This is browser lifecycle recovery only; no Guide decision is
  // changed and no timeout is increased.
  try {
    await ensureContentScript(tabId, frameId);
    return await chrome.tabs.sendMessage(tabId, payload, { frameId });
  } catch (error) {
    if (!isTransientReceiverError(error)) throw error;
    return null;
  }
}

async function productionCandidateTabs() {
  const tabs = await chrome.tabs.query({});
  return tabs.filter(tab =>
    tab.id != null &&
    (String(tab.url || "").startsWith("http://") || String(tab.url || "").startsWith("https://"))
  );
}

function applicationContextMatcherMatches(tab, matcher) {
  const kind = String(matcher?.kind || "").trim().toLowerCase();
  const value = String(matcher?.value || "");
  const title = String(tab.title || "");
  const url = String(tab.url || "");
  switch (kind) {
    case "titleequals": return title === value;
    case "titlecontains": return title.includes(value);
    case "urlequals": return url === value;
    case "urlcontains": return url.includes(value);
    case "urlhost":
      try { return new URL(url).host === value; }
      catch { return false; }
    default: throw new Error("Unsupported Web Application Context matcher '" + matcher?.kind + "'.");
  }
}

async function resolveProductionContextTab(contextKey, context) {
  if (!contextKey || !context)
    throw new Error("DAP Web command is missing its persisted Application Context.");
  if (closedProductionContexts.has(contextKey))
    throw new Error("DAP_WEB_TARGET_CLOSED: The bound application tab was closed.");

  const retainedTabId = productionContextTabs.get(contextKey);
  if (retainedTabId != null) {
    try {
      const tab = await chrome.tabs.get(retainedTabId);
      if (tab?.id != null) return retainedTabId;
    } catch {
      productionContextTabs.delete(contextKey);
      closedProductionContexts.add(contextKey);
      throw new Error("DAP_WEB_TARGET_CLOSED: The bound application tab was closed.");
    }
    productionContextTabs.delete(contextKey);
  }

  const matchers = Array.isArray(context.matchers) ? context.matchers : [];
  if (matchers.length === 0)
    throw new Error("DAP Application Context '" + contextKey + "' has no matchers.");

  const candidates = await productionCandidateTabs();
  const matches = candidates.filter(tab =>
    matchers.every(matcher => applicationContextMatcherMatches(tab, matcher))
  );

  if (matches.length === 0) return null;
  if (matches.length > 1) {
    const details = matches.map(tab => tab.id + ":" + (tab.url || "<no-url>")).join(", ");
    throw new Error(
      "DAP Application Context '" + contextKey + "' is ambiguous: " + matches.length +
      " browser tabs match its persisted matchers. DAP will not guess. matches=[" + details + "]"
    );
  }

  productionContextTabs.set(contextKey, matches[0].id);
  return matches[0].id;
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

    if (identified && identified.tabId === tabId) {
      frameId = identified.frameId;
      continue;
    }

    // The DOM probe has already proven exactly one iframe element. Chrome can
    // replace/promote that iframe while retaining its original browsing-context
    // name, so frame names are not a stable identity. Map the resolved DOM frame
    // to webNavigation by parent relationship + exact live document URL, and
    // accept it only when that mapping is unique. Never guess.
    const frameUrl = probe.result.frameUrl;
    if (frameUrl) {
      const frames = await chrome.webNavigation.getAllFrames({ tabId }) || [];
      const matches = frames.filter(frame =>
        frame.parentFrameId === frameId &&
        frame.url === frameUrl
      );

      if (matches.length === 1) {
        frameId = matches[0].frameId;
        continue;
      }

      if (matches.length > 1) {
        throw new Error(
          "DAP frame mapping is ambiguous for live iframe URL '" +
          frameUrl + "' under parent frame " + frameId + "."
        );
      }
    }

    throw new Error("DAP could not map the resolved iframe element to a unique browser frame.");
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
        response: {
          ok: true,
          result: {
            tabId,
            url: tab.url || "",
            testDriverVersion: TEST_DRIVER_VERSION,
            extensionVersion: chrome.runtime.getManifest().version
          }
        }
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
    const envelope = { ...(message.command || {}) };
    const contextual = envelope.type === "contextualCommand";
    const command = contextual ? { ...(envelope.command || {}) } : envelope;
    const tabId = message.tabId ?? (
      message.sessionId
        ? await resolveTestTab(String(message.sessionId))
        : contextual
          ? await resolveProductionContextTab(envelope.applicationContextKey, envelope.applicationContext)
          : null
    );
    const originalFramePath = Array.isArray(command.framePath)
      ? command.framePath.map(locator => ({ ...locator }))
      : [];

    // "No matching application tab yet" is a normal learner state, not a
    // transport failure. The Runtime may start before the target application,
    // or the application may be navigating between persisted contexts. Return
    // semantic NotFound/inactive results so the learner reconciliation loop can
    // keep waiting. Ambiguity remains a hard error and is handled above.
    if (tabId == null) {
      if (command.type === "hideBubble") {
        postAdapterResponse(requestId, { ok: true, result: { status: "hidden" } }, null, null);
        return;
      }
      if (command.type === "isContextActive") {
        postAdapterResponse(requestId, { ok: true, result: { active: false } }, null, null);
        return;
      }
      if (command.type === "waitForDomQuiet") {
        postAdapterResponse(requestId, { ok: true, result: { stable: false } }, null, null);
        return;
      }

      postAdapterResponse(
        requestId,
        { ok: true, result: { status: "notFound", count: 0 } },
        null,
        null
      );
      return;
    }

    // Bring the persisted, uniquely resolved production target to the foreground
    // once per learner session, without stealing focus on later reconciliation.
    if (contextual && !message.sessionId &&
        ((command.type === "ensureBubble" && command.visible !== false) || command.type === "showCenteredStep") &&
        !activatedProductionSessions.has(String(message.sessionId || "production") + ":" + envelope.applicationContextKey)) {
      const tab = await chrome.tabs.get(tabId);
      if (tab.windowId == null)
        throw new Error("Resolved DAP Web target has no browser window.");
      await chrome.tabs.update(tabId, { active: true });
      await chrome.windows.update(tab.windowId, { focused: true, state: "normal" });
      const focusedWindow = await chrome.windows.getLastFocused();
      if (focusedWindow.id !== tab.windowId || !focusedWindow.focused)
        throw new Error("DAP could not bring the resolved browser window to the foreground.");
      const activeTab = await chrome.tabs.get(tabId);
      if (activeTab.title && port)
        port.postMessage({ type: "adapterEvent", payload: { type: "activate-target-window", title: activeTab.title, applicationContextKey: envelope.applicationContextKey } });
      activatedProductionSessions.add(String(message.sessionId || "production") + ":" + envelope.applicationContextKey);
    }

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

    if (response == null) {
      if (command.type === "waitForDomQuiet") {
        postAdapterResponse(requestId, { ok: true, result: { stable: false } }, tabId, frameId);
        return;
      }
      if (command.type === "isContextActive") {
        postAdapterResponse(requestId, { ok: true, result: { active: false } }, tabId, frameId);
        return;
      }
      postAdapterResponse(
        requestId,
        { ok: true, result: { status: "notFound", count: 0 } },
        tabId,
        frameId
      );
      return;
    }

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
          visible: command.visible,
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
