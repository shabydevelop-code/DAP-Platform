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
Object.assign(instructorLabels, {
  "שם הלומדה": "Guide name",
  "מזהה": "ID",
  "מספר שלבים": "Number of steps",
  "לא נמצאו לומדות. ניתן ליצור לומדה חדשה.": "No guides found. Create a new guide.",
  "אין שלבים להצגה": "No steps to display",
  "טוען את פרטי השלב...": "Loading step details...",
  "לא הוגדר תנאי": "No condition defined",
  "כותרת במסך": "Screen heading",
  "הרכיב שנבחר": "Selected element",
  "מופיע במסך": "Appears on screen",
  "תנאי השלמה": "Completion condition",
  "לא משויך": "Not assigned",
  "שלב פעיל": "Active step",
  "שלב מושבת": "Disabled step",
  "שכפל שלב": "Duplicate step",
  "מחיקת שלב": "Delete step",
  "בדיקה משלב זה": "Test from this step",
  "אפליקציית יעד": "Target application",
  "הנחיה למשתמש": "Learner instruction",
  "לא הוגדרה הנחיה": "No instruction defined",
  "עריכת תוכן השלב": "Edit step content",
  "רכיב במסך": "Screen element",
  "עדיין לא נבחר רכיב לשלב הזה.": "No element selected for this step yet.",
  "הפעלת ממפה רכיבים": "Start element picker",
  "הוספת רכיב במסך": "Add screen element",
  "יש לבחור תחילה רכיב במסך.": "Select a screen element first.",
  "מתי ממשיכים לשלב הבא?": "When to continue to the next step?",
  "יוגדר לאחר בחירת הרכיב.": "Available after selecting an element.",
  "ערך צפוי:": "Expected value:",
  "עריכה": "Edit",
  "הגדרת תנאי התקדמות": "Set progression condition",
  "הסרה": "Remove",
  "תנאי השלמה נוספים": "Additional completion conditions",
  "תנאי:": "Condition:",
  "בחירת רכיב אחר": "Select another element",
  "הוספת תנאי השלמה": "Add completion condition",
  "רכיב שלא זוהה": "Unidentified element",
  "הערך המלא": "Full value",
  "מספר בלבד": "Numbers only",
  "חילוץ מותאם אישית": "Custom extraction",
  "טקסט הרכיב שנבחר": "Selected element text",
  "הוספת פעולה למשתנה": "Add variable action",
  "עריכת פעולת משתנה": "Edit variable action",
  "משתנה לעדכון": "Variable to update",
  "אופן חילוץ הערך": "Value extraction method",
  "ביטוי לחילוץ מותאם אישית": "Custom extraction expression",
  "לחיצה על הרכיב": "Click the element",
  "הוזן ערך בשדה": "Field contains a value",
  "השדה מכיל ערך מסוים": "Field matches a specific value",
  "תנאי מתקדם שהוגדר במערכת": "Advanced condition defined in the system",
  "ערך לזיהוי הרכיב": "Element identification value",
  "חוק השלמה": "Completion rule",
  "הרכיב מופיע במסך": "Element appears on screen",
  "הרכיב מכיל ערך מסוים": "Element contains a specific value",
  "מתי המערכת תעבור לשלב הבא?": "When should the system advance?",
  "אפשר לשלב טקסט ומשתנים בערך הצפוי.": "Text and variables can be combined in the expected value.",
  "משתנה": "Variable",
  "הוסף משתנה": "Insert variable",
  "אישור רכיב במסך": "Confirm screen element",
  "ערך לקליטה": "Value to capture",
  "ביטוי לחילוץ (Regex)": "Extraction expression (Regex)",
  "אם הוגדרה קבוצת לכידה, יישמר תוכן הקבוצה הראשונה.": "If a capture group is defined, the first group is saved.",
  "שדה טקסט רב־שורות": "Multiline text field",
  "שדה קלט": "Input field",
  "רשימת בחירה": "Dropdown",
  "כפתור": "Button",
  "קישור": "Link",
  "רכיב": "Element",
  "הערך שנקלט": "Captured value",
  "זיהוי לפי תוכן": "Identify by content",
  "לא נקלט ערך": "No value captured",
  "שומר רכיב...": "Saving element...",
  "הרכיב נשמר בהצלחה.": "Element saved successfully.",
  "הבחירה בוטלה.": "Selection canceled.",
  "הסר": "Remove",
  "טרם נוספו אפליקציות ללומדה.": "No applications added to this guide.",
  "ללא ערך התחלתי": "No initial value",
  "עדיין לא הוגדרו משתנים בלומדה.": "No variables defined in this guide.",
  "עריכת משתנה": "Edit variable",
  "הוספת משתנה": "Add variable",
  "שם המשתנה": "Variable name",
  "ערך התחלתי (לא חובה)": "Initial value (optional)",
  "לא ניתן לשנות שם של משתנה שנמצא בשימוש בלומדה.": "Cannot rename a variable used in this guide.",
  "השם מוצג לעורך; מזהה המשתנה נוצר אוטומטית.": "The name is shown to the author; the variable ID is generated automatically.",
  "כל ערך": "Any value",
  "הערך המלא בזמן ההרצה": "Full value at runtime",
  "לא נמצאה התאמה": "No match found",
  "לא נמצא מספר": "No number found",
  "ביטוי לא תקין": "Invalid expression",
  "יעד פעולה": "Action target",
  "מזהה אימות": "Verification identity",
  "ממפה רכיבים": "Element picker",
  "שמירה": "Save",
  "ביטול": "Cancel"
});
const reverseInstructorLabels = Object.fromEntries(
  Object.entries(instructorLabels).map(([he, en]) => [en, he]));
const originalText = new WeakMap();
const authoredContentSelector = "[contenteditable],textarea,input,#detail-name,.step-summary-rich,.step-body,.variable-name,.variable-value,.guide-name,.guide-identity,.validation-expected-preview";
const originalAttributes = new WeakMap();
let instructorLanguage = "he";
function translateInstructor(root = document) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let node;
  while ((node = walker.nextNode())) {
    if (node.parentElement?.closest("script,style," + authoredContentSelector)) continue;
    const source = originalText.get(node) ?? node.nodeValue;
    if (!originalText.has(node)) originalText.set(node, source);
    const trimmed = source.trim();
    if (!instructorLabels[trimmed] && !reverseInstructorLabels[trimmed]) continue;
    const translation = instructorLanguage === "en"
      ? (instructorLabels[trimmed] ?? trimmed)
      : (reverseInstructorLabels[trimmed] ?? trimmed);
    const next = source.replace(trimmed, translation);
    if (node.nodeValue !== next) node.nodeValue = next;
  }
  for (const element of root.querySelectorAll("[placeholder],[aria-label],[title]")) {
    for (const attr of ["placeholder", "aria-label", "title"]) {
      if (!element.hasAttribute(attr)) continue;
      let originals = originalAttributes.get(element);
      if (!originals) { originals = {}; originalAttributes.set(element, originals); }
      if (!(attr in originals)) originals[attr] = element.getAttribute(attr);
      const source = originals[attr];
      const next = instructorLanguage === "en" ? (instructorLabels[source] ?? source) : source;
      if (element.getAttribute(attr) !== next) element.setAttribute(attr, next);
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
  // Dynamic Inspector and dialogs are rendered after initial page load.
  // Observe inserted UI while excluding editable and authored guide content.
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === "characterData") {
        const node = record.target;
        if (node.parentElement && !node.parentElement.closest(authoredContentSelector))
          translateInstructor(node.parentElement);
      } else {
        for (const node of record.addedNodes) {
          if (node.nodeType === Node.ELEMENT_NODE && !node.matches(authoredContentSelector))
            translateInstructor(node);
        }
      }
    }
  });
  observer.observe(document.body, { subtree: true, childList: true, characterData: true });
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
