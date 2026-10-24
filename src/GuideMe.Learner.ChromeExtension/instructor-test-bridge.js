// Explicit local Instructor test launches wake the existing production transport.
(() => {
  if (!['localhost', '127.0.0.1'].includes(location.hostname) || location.pathname !== '/') return;
  document.addEventListener('guideme:instructor-test-launch', () => {
    try {
      const pending = chrome.runtime.sendMessage({ type: 'guideMe-runtime-ready' });
      if (pending?.catch) pending.catch(() => {});
    } catch { /* Extension may be reloading. */ }
  });
})();
