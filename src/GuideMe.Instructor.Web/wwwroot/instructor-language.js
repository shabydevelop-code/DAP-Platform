"use strict";
// Instructor interface catalogs are loaded from C:\\GuideMe\\Localization via the API.
// Guide-authored text and captured values are never translated.
const instructorLabels = {};
let reverseInstructorLabels = {};
// Translate fixed UI messages and prefixes without altering appended guide values.
window.instructorT = function (message) {
  const source = String(message ?? "");
  if (instructorLanguage !== "en") return source;
  if (instructorLabels[source]) return instructorLabels[source];
  const prefix = Object.keys(instructorLabels)
    .filter(key => key.length >= 5 && source.startsWith(key))
    .sort((a, b) => b.length - a.length)[0];
  return prefix ? instructorLabels[prefix] + source.slice(prefix.length) : source;
};
const originalText = new WeakMap();
const translatedText = new WeakMap();
const authoredContentSelector = "[contenteditable],textarea,input,#detail-name,.step-summary-rich,.step-body,.variable-name,.variable-value,.guide-name,.guide-identity,.validation-expected-preview,#guides-table tbody td:not(:has(button)),.context-name,.variable-initial-value,.captured-object-value";
const originalAttributes = new WeakMap();
let instructorLanguage = "he";
function translateInstructor(root = document) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let node;
  while ((node = walker.nextNode())) {
    if (node.parentElement?.closest("script,style," + authoredContentSelector)) continue;
    // When the app reuses a text node, do not restore a stale translation.
    const previousTranslation = translatedText.get(node);
    if (!originalText.has(node) || (previousTranslation !== undefined && node.nodeValue !== previousTranslation))
      originalText.set(node, node.nodeValue);
    const source = originalText.get(node);
    const trimmed = source.trim();
    if (!instructorLabels[trimmed] && !reverseInstructorLabels[trimmed]) continue;
    const translation = instructorLanguage === "en"
      ? (instructorLabels[trimmed] ?? trimmed)
      : (reverseInstructorLabels[trimmed] ?? trimmed);
    const next = source.replace(trimmed, translation);
    translatedText.set(node, next);
    if (node.nodeValue !== next) node.nodeValue = next;
  }
  const elements = root.querySelectorAll ? Array.from(root.querySelectorAll("[placeholder],[aria-label],[title]")) : [];
  if (root.nodeType === Node.ELEMENT_NODE && root.matches("[placeholder],[aria-label],[title]")) elements.unshift(root);
  for (const element of elements) {
    for (const attr of ["placeholder", "aria-label", "title"]) {
      if (!element.hasAttribute(attr)) continue;
      let originals = originalAttributes.get(element);
      if (!originals) { originals = {}; originalAttributes.set(element, originals); }
      if (!(attr in originals) || (originals[attr + "Translated"] !== undefined && element.getAttribute(attr) !== originals[attr + "Translated"]))
        originals[attr] = element.getAttribute(attr);
      const source = originals[attr];
      const next = instructorLanguage === "en" ? (instructorLabels[source] ?? source) : source;
      originals[attr + "Translated"] = next;
      if (element.getAttribute(attr) !== next) element.setAttribute(attr, next);
    }
  }
  document.documentElement.lang = instructorLanguage;
  document.documentElement.dir = instructorLanguage === "he" ? "rtl" : "ltr";
}
document.addEventListener("DOMContentLoaded", async () => {
  const select = document.getElementById("instructor-language");
  try {
    const catalogResponse = await fetch("/api/instructor/localization/en", { cache: "no-store" });
    if (!catalogResponse.ok) throw new Error("Failed to load Instructor translations");
    const englishCatalog = await catalogResponse.json();
    const hebrewResponse = await fetch("/api/instructor/localization/he", { cache: "no-store" });
    if (!hebrewResponse.ok) throw new Error("Failed to load Hebrew Instructor translations");
    const hebrewCatalog = await hebrewResponse.json();
    for (const [key, value] of Object.entries(englishCatalog)) {
      if (Object.prototype.hasOwnProperty.call(hebrewCatalog, key))
        instructorLabels[hebrewCatalog[key]] = value;
    }
    reverseInstructorLabels = Object.fromEntries(Object.entries(instructorLabels).map(([he, en]) => [en, he]));
    const response = await fetch("/api/instructor/language");
    if (!response.ok) throw new Error("Failed to load language");
    instructorLanguage = (await response.json()).language;
  } catch (error) { console.error("Instructor language:", error); }
  select.value = instructorLanguage;
  translateInstructor();
  // Dynamic Inspector and dialogs are rendered after initial page load.
  // Observe inserted UI while excluding editable and authored guide content.
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === "characterData") {
        const node = record.target;
        if (node.parentElement && !node.parentElement.closest(authoredContentSelector))
          translateInstructor(node.parentElement);
      } else if (record.type === "attributes") {
        if (!record.target.closest(authoredContentSelector)) translateInstructor(record.target);
      } else {
        for (const node of record.addedNodes) {
          if (node.nodeType === Node.ELEMENT_NODE && !node.matches(authoredContentSelector))
            translateInstructor(node);
        }
      }
    }
  });
  observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["placeholder", "aria-label", "title"] });
  select.addEventListener("change", async () => {
    const next = select.value;
    try {
      const response = await fetch("/api/instructor/language", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ language: next })
      });
      if (!response.ok) throw new Error("Failed to save language");
      instructorLanguage = next;
      translateInstructor();
    } catch (error) {
      select.value = instructorLanguage;
      console.error("Instructor language:", error);
    }
  });
});
