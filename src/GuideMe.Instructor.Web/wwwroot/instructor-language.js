"use strict";
// Instructor UI language is independent of the Learner's selected language.
// Translate only interface chrome; never translate user-authored guide content.
const instructorLabels = {
  "דלג לתוכן הראשי": "Skip to main content",
  "שפה": "Language",
  "עברית": "Hebrew",
  "חזרה לספריית הלומדות": "Back to guide library",
  "תוסף: בודק חיבור": "Extension: checking connection",
  "ספריית לומדות": "Guide library",
  "בחר לומדה כדי לצפות בשלבים ולנהל אפליקציות.": "Select a guide to view its steps and manage applications.",
  "＋ יצירת לומדה": "＋ Create guide",
  "בדיקת לומדה": "Test guide",
  "שינוי שם": "Rename",
  "מחיקת לומדה": "Delete guide",
  "שלבי הלומדה": "Guide steps",
  "אפליקציות ומשתנים": "Applications and variables",
  "＋ הוספת שלב": "＋ Add step",
  "בחר שלב להצגת הפרטים שלו": "Select a step to view its details",
  "אפליקציות": "Applications",
  "הגדרת אפליקציות היעד ומשתנים לשימוש בשלבי הלומדה.": "Configure target applications and variables for guide steps.",
  "＋ הוספת אפליקציה": "＋ Add application",
  "משתנים": "Variables",
  "הגדרת משתנים לשימוש בשלבים. ערכים שנקלטים בזמן הלומדה נשמרים להרצה בלבד.": "Define variables for guide steps. Captured values are kept only for the current run.",
  "＋ הוספת משתנה": "＋ Add variable",
  "ביטול": "Cancel",
  "שמירה": "Save",
  "חיפוש לפי שם או מזהה...": "Search by name or ID...",
  "חיפוש שלב...": "Search steps...",
  "חיפוש לומדה לפי שם או מזהה": "Search guides by name or ID",
  "חיפוש שלב בלומדה": "Search guide steps",
  "רשימת שלבים": "Step list",
  "ניווט בלומדה": "Guide navigation",
  "סגירת חלון העריכה": "Close editor"
};
const reverseInstructorLabels = Object.fromEntries(
  Object.entries(instructorLabels).map(([he, en]) => [en, he]));
const originalText = new WeakMap();
const originalAttributes = new WeakMap();
let instructorLanguage = "he";
function translateInstructor(root = document) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let node;
  while ((node = walker.nextNode())) {
    if (node.parentElement?.closest("script,style,textarea,[contenteditable],#guides-table,#step-list,#step-inspector,#contexts-list,#guide-variables-list,#dialog-fields,#notice")) continue;
    const source = originalText.get(node) ?? node.nodeValue;
    if (!originalText.has(node)) originalText.set(node, source);
    const trimmed = source.trim();
    if (!instructorLabels[trimmed] && !reverseInstructorLabels[trimmed]) continue;
    const translation = instructorLanguage === "en"
      ? (instructorLabels[trimmed] ?? trimmed)
      : (reverseInstructorLabels[trimmed] ?? trimmed);
    node.nodeValue = source.replace(trimmed, translation);
  }
  for (const element of root.querySelectorAll("[placeholder],[aria-label]")) {
    for (const attr of ["placeholder", "aria-label"]) {
      if (!element.hasAttribute(attr)) continue;
      let originals = originalAttributes.get(element);
      if (!originals) { originals = {}; originalAttributes.set(element, originals); }
      if (!(attr in originals)) originals[attr] = element.getAttribute(attr);
      const source = originals[attr];
      element.setAttribute(attr, instructorLanguage === "en" ? (instructorLabels[source] ?? source) : source);
    }
  }
  document.documentElement.lang = instructorLanguage;
  document.documentElement.dir = instructorLanguage === "he" ? "rtl" : "ltr";
}
document.addEventListener("DOMContentLoaded", async () => {
  const select = document.getElementById("instructor-language");
  try {
    const response = await fetch("/api/instructor/language");
    if (!response.ok) throw new Error("Failed to load language");
    instructorLanguage = (await response.json()).language;
  } catch (error) { console.error("Instructor language:", error); }
  select.value = instructorLanguage;
  translateInstructor();
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
