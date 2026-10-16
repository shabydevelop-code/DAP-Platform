const status = document.getElementById("status");
document.getElementById("connect").addEventListener("click", async () => {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id || !/^https?:/.test(tab.url || "")) throw new Error("פתח אתר בלשונית הפעילה.");
    const response = await chrome.runtime.sendMessage({
      type: "guideme-instructor-pair",
      token: document.getElementById("token").value.trim(),
      tabId: tab.id
    });
    if (!response?.ok) throw new Error(response?.error || "Pairing failed");
    await chrome.scripting.executeScript({
      target: { tabId: tab.id, allFrames: true },
      files: ["instructor-capture.js"]
    });
    status.textContent = "מחובר ללשונית. לחץ ״התחל שלב״ בחלון ה־Instructor.";
  } catch (error) {
    status.textContent = String(error.message || error);
  }
});
document.getElementById("disconnect").addEventListener("click", async () => {
  await chrome.runtime.sendMessage({ type: "guideme-instructor-stop" });
  status.textContent = "מנותק.";
});
