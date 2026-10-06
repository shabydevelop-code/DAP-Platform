importScripts("service-worker.js");

// E2E-only frame routing must follow the live iframe element, not window.name.
// TestCRM intentionally replaces/promotes iframe elements and the browsing-context
// name can remain stale even when the DOM frame element has already been renamed.
// Production guide routing is unchanged; this override is used only by the
// extension-native test driver path that calls resolveTestFrame().
const dapResolveTestFrameByName = resolveTestFrame;
resolveTestFrame = async function(tabId, frameName) {
  const selector = frameName === "dap-content"
    ? "#content-frame"
    : frameName === "dap-header"
      ? "#header-frame"
      : null;

  if (!selector)
    return await dapResolveTestFrameByName(tabId, frameName);

  const resolved = await resolveFramePath(
    tabId,
    [{ strategy: "css", value: selector }],
    "test-live-frame-" + frameName + "-" + crypto.randomUUID()
  );

  if (resolved.ok)
    return resolved.frameId;

  throw new Error(
    "DAP test driver could not resolve live frame '" + frameName +
    "' through shell iframe '" + selector + "'."
  );
};
