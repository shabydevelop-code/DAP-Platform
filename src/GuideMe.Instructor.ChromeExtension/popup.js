const status = document.getElementById("status");
const pairingControls = document.getElementById("pairing-controls");
const disconnectButton = document.getElementById("disconnect");

function showConnectionState(connected) {
  pairingControls.hidden = connected;
  disconnectButton.hidden = !connected;
}

async function refreshConnectionStatus() {
  try {
    const [{ instructorPairing }, [tab]] = await Promise.all([
      chrome.storage.session.get("instructorPairing"),
      chrome.tabs.query({ active: true, currentWindow: true })
    ]);
    showConnectionState(Boolean(instructorPairing));
    if (!instructorPairing) {
      status.textContent = "לא מחובר.";
    } else if (tab?.id === instructorPairing.tabId) {
      status.textContent = "מחובר ללשונית זו. לחץ ״התחל שלב״ בחלון ה־Instructor.";
    } else {
      status.textContent = "קיים חיבור ללשונית אחרת. עבור אליה כדי להקליט פעולות.";
    }
  } catch (error) {
    showConnectionState(false);
    status.textContent = String(error.message || error);
  }
}

document.getElementById("connect").addEventListener("click", async () => {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id || !/^https?:/.test(tab.url || "")) throw new Error("פתח אתר בלשונית הפעילה.");
    const response = await chrome.runtime.sendMessage({
      type: "guideme-instructor-auto-pair",
      tabId: tab.id
    });
    if (!response?.ok) throw new Error(response?.error || "Pairing failed");
    try {
      await chrome.scripting.executeScript({
        target: { tabId: tab.id, allFrames: true },
        files: ["content-capture.js"]
      });
    } catch (error) {
      await chrome.runtime.sendMessage({ type: "guideme-instructor-stop" });
      throw error;
    }
    await refreshConnectionStatus();
  } catch (error) {
    status.textContent = String(error.message || error);
  }
});

document.getElementById("disconnect").addEventListener("click", async () => {
  try {
    const response = await chrome.runtime.sendMessage({ type: "guideme-instructor-stop" });
    if (!response?.ok) throw new Error(response?.error || "Disconnect failed");
    tokenInput.value = "";
    await refreshConnectionStatus();
  } catch (error) {
    status.textContent = String(error.message || error);
  }
});

refreshConnectionStatus();
