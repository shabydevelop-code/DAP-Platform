// Isolated-world bridge: explicitly verify that the Learner Native Host is
// responsive before Instructor starts the production GuideMe process.
(() => {
  if (location.origin !== 'http://127.0.0.1:53922') return;
  document.addEventListener('guideme:instructor-test-ping', async event => {
    const requestId = event.detail?.requestId;
    if (typeof requestId !== 'string' || !requestId) return;
    let result;
    try {
      result = await chrome.runtime.sendMessage({ type: 'guideMe-instructor-test-ping' });
    } catch (error) {
      result = { ok: false, error: String(error?.message || error) };
    }
    document.dispatchEvent(new CustomEvent('guideme:instructor-test-pong', {
      detail: { requestId, ok: result?.ok === true, error: result?.error || null }
    }));
  });
})();
