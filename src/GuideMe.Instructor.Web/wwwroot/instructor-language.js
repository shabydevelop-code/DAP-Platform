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
Object.assign(instructorLabels, {
  "הוספת שלב": "Add step",
  "יצירת לומדה": "Create guide",
  "שם": "Name",
  "סוג": "Type",
  "פעולה": "Action",
  "פעולות": "Actions",
  "סטטוס": "Status",
  "שלב": "Step",
  "שלבים": "Steps",
  "פעיל": "Active",
  "מושבת": "Disabled",
  "אישור": "Confirm",
  "סגירה": "Close",
  "חזרה": "Back",
  "חיפוש": "Search",
  "הוספה": "Add",
  "הוספת": "Add",
  "עדכון": "Update",
  "מחיקה": "Delete",
  "החלפה": "Replace",
  "בחר": "Select",
  "בחירה": "Select",
  "תיאור": "Description",
  "כתובת": "Address",
  "אתר": "Website",
  "דפדפן": "Browser",
  "חלון": "Window",
  "מזהה רכיב": "Element ID",
  "שם רכיב": "Element name",
  "תוכן": "Content",
  "ערך": "Value",
  "תנאי": "Condition",
  "בדיקה": "Test",
  "עריכת שלב": "Edit step",
  "עריכת לומדה": "Edit guide",
  "פרטי שלב": "Step details",
  "תנאי התקדמות": "Progression condition",
  "ערך קבוע": "Fixed value",
  "שם אפליקציה": "Application name",
  "סוג אפליקציה": "Application type",
  "רכיבים נוספים": "Additional elements",
  "פרטי רכיב": "Element details",
  "תוסף מחובר": "Extension connected",
  "תוסף מנותק": "Extension disconnected",
  "ממתין לחיבור": "Waiting for connection",
  "מחובר": "Connected",
  "מנותק": "Disconnected",
  "שמירה בוצעה": "Saved",
  "השמירה נכשלה": "Save failed",
  "ללא ערך": "No value",
  "לא הוקצה ערך": "No value assigned",
  "לא הוגדר": "Not configured",
  "לא נבחר": "Not selected",
  "הוספת תנאי": "Add condition",
  "הגדרות": "Settings",
  "חלונות פתוחים": "Open windows",
  "לשוניות פתוחות": "Open tabs",
  "בחר אפליקציה": "Select application",
  "בחירת אפליקציה": "Select application",
  "הגדרת אפליקציה": "Configure application",
  "שדה חובה": "Required field",
  "חובה": "Required",
  "לא חובה": "Optional",
  "שם משתנה": "Variable name",
  "משתנה חדש": "New variable",
  "ערך התחלתי": "Initial value",
  "שמירת שינויים": "Save changes",
  "אישור בחירה": "Confirm selection",
  "ביטול בחירה": "Cancel selection",
  "תצוגה מקדימה": "Preview",
  "יציאה מתצוגה מקדימה": "Exit preview",
  "איפוס": "Reset",
  "עריכת הנחיה": "Edit instruction",
  "הנחיה": "Instruction",
  "טקסט ההנחיה": "Instruction text",
  "בדיקת שלב": "Test step",
  "הצגת פרטים": "Show details",
  "פרטים נוספים": "Additional details",
  "מקור": "Source",
  "חילוץ": "Extraction",
  "זיהוי": "Identification",
  "זיהוי רכיב": "Element identification",
  "ערך זיהוי": "Identification value",
  "טקסט": "Text",
  "כותרת": "Heading",
  "שדה": "Field",
  "כפתור": "Button",
  "קישור": "Link",
  "רשימה": "List",
  "טבלה": "Table",
  "שורת טבלה": "Table row",
  "תא בטבלה": "Table cell",
  "לחיצה": "Click",
  "הקלדה": "Typing",
  "הופעה": "Appearance",
  "שינוי ערך": "Value changed",
  "כל ערך": "Any value",
  "ערך מסוים": "Specific value",
  "ללא תנאי": "No condition",
  "ללא רכיב": "No element",
  "המשך": "Continue",
  "הקודם": "Previous",
  "הבא": "Next",
  "סיום": "Finish",
  "תצוגת שלבים": "Steps view",
  "ספריית לומדות": "Guide library",
  "לא נמצאו תוצאות": "No results found",
  "אין נתונים להצגה": "No data to display",
  "מוכן": "Ready",
  "טוען...": "Loading...",
  "מעבד...": "Processing..."
});
Object.assign(instructorLabels, {
  "יש שינויים שלא נשמרו. לצאת בלי לשמור אותם?": "There are unsaved changes. Exit without saving?",
  "יש להזין ביטוי לחילוץ.": "Enter an extraction expression.",
  "פעולת המשתנה עודכנה.": "Variable action updated.",
  "להסיר את פעולת המשתנה?": "Remove this variable action?",
  "השלב המשוכפל לא נמצא ברשימת השלבים.": "The duplicated step was not found in the step list.",
  "השלב שוכפל ונפתח לעריכה.": "Step duplicated and opened for editing.",
  "ללא הנחיה": "No instruction",
  "השלב, הרכיבים וכללי ההתקדמות הייחודיים לו יימחקו לצמיתות.": "The step, its elements and its progression rules will be permanently deleted.",
  "יעד הפעולה עודכן.": "Action target updated.",
  "להסיר את מזהה האימות?": "Remove the verification identity?",
  "מזהה האימות הוסר.": "Verification identity removed.",
  "להסיר את תנאי ההשלמה?": "Remove the completion condition?",
  "תנאי ההשלמה הוסר.": "Completion condition removed.",
  "לרכיב זה אין ערך זיהוי טקסטואלי שניתן לערוך. ניתן לבחור רכיב אחר.": "This element has no editable text identification value. Select another element.",
  "לא ניתן להסיר את יעד הפעולה כאשר קיימים רכיבים נוספים. יש להגדיר תחילה רכיב אחר כיעד פעולה.": "Cannot remove the action target while other elements exist. Assign another action target first.",
  "להסיר את הרכיב מהשלב? תנאי האימות ותנאי ההשלמה התלויים בו יוסרו גם הם.": "Remove this element from the step? Its dependent verification and completion conditions will also be removed.",
  "הרכיב הוסר מהשלב.": "Element removed from the step.",
  "להסיר את תנאי ההתקדמות מהשלב?": "Remove the progression condition from the step?",
  "תנאי ההתקדמות הוסר.": "Progression condition removed.",
  "יש להזין ערך צפוי.": "Enter an expected value.",
  "יש להזין ערך לזיהוי הרכיב.": "Enter an element identification value.",
  "תנאי ההשלמה עודכן.": "Completion condition updated.",
  "סימון משתנה לא מוכר.": "Unknown variable token.",
  "תנאי ההתקדמות עודכן.": "Progression condition updated.",
  "לשני משתנים יש אותו ערך מוצג.": "Two variables have the same displayed value.",
  "יש לבחור משתנה בשלמותו או למקם את הסמן מחוץ לסימון.": "Select the entire variable or place the cursor outside the token.",
  "לא נמצאה לשונית מתאימה. פתח את האפליקציה באתר ונסה שוב.": "No matching tab found. Open the application and try again.",
  "בחירת הלשונית בוטלה.": "Tab selection canceled.",
  "בחירת לשונית אינה תקינה.": "Invalid tab selection.",
  "נמצאו מספר חלונות תואמים.": "Multiple matching windows found.",
  "לא נמצא חלון מתאים לאפליקציה.": "No matching application window found.",
  "סביבת היעד אינה נתמכת.": "Unsupported target environment.",
  "תנאי השלמה נוסף אינו נתמך כרגע בסביבת יעד זו.": "Additional completion conditions are not supported for this target environment.",
  "אזור אישור הרכיב חסר בממשק.": "The element confirmation area is missing from the interface.",
  "כרטיס הרכיב שנבחר להחלפה אינו זמין.": "The element card selected for replacement is unavailable.",
  "מפעיל ממפה רכיבים…": "Starting element picker…",
  "Windows לא אישר העברת פוקוס אוטומטית. הממפה פעיל; עבור לחלון האפליקציה כדי לבחור רכיב.": "Windows did not allow automatic focus. The picker is active; switch to the application window to select an element.",
  "איתור הרכיב בוטל. ניתן להפעיל שוב את הממפה.": "Element picking canceled. You can restart the picker.",
  "אין כרגע תקשורת עם תוסף Instructor. בדוק שהוא פעיל ב־Chrome.": "No connection to the Instructor extension. Verify it is enabled in Chrome.",
  "ממתין לאישור הפעלת הממפה בלשונית שנבחרה. אם לא מופיעה מסגרת בחלון היעד, ניתן לבטל ולנסות שוב.": "Waiting for the picker to start in the selected tab. If no outline appears, cancel and retry.",
  "ממפה פעיל: העבר עכבר מעל רכיב ולחץ Ctrl+Shift+Space לבחירה (Esc לביטול).": "Picker active: hover over an element and press Ctrl+Shift+Space to select (Esc to cancel).",
  "מפעיל בחירת רכיב…": "Starting element selection…",
  "יש למלא שדה זה לפני השמירה.": "Complete this field before saving.",
  "יש לבחור משתנה לעדכון.": "Select a variable to update.",
  "תוכן זיהוי הרכיב ארוך מדי (עד 400 תווים).": "Element identification content is too long (maximum 400 characters).",
  "רכיב מקור המשתנה נשמר ללא שינוי רכיב הפעולה.": "Variable source saved without changing the action target.",
  "מזהה האימות הוחלף.": "Verification identity replaced.",
  "מזהה האימות נשמר.": "Verification identity saved.",
  "הרכיב הוחלף בהצלחה. יש להגדיר מחדש מתי ממשיכים לשלב הבא.": "Element replaced. Configure the progression condition again.",
  "תנאי ההשלמה נשמר.": "Completion condition saved.",
  "רכיב היעד נשמר. ניתן להגדיר תנאי התקדמות בנפרד.": "Target element saved. You can configure progression separately.",
  "המשתנה נמחק.": "Variable deleted.",
  "המשתנה עודכן.": "Variable updated.",
  "המשתנה נוסף ללומדה.": "Variable added to guide.",
  "יצירת לומדה חדשה": "Create new guide",
  "יש לבחור לפחות אפליקציה אחת.": "Select at least one application.",
  "שינוי שם הלומדה": "Rename guide",
  "שם הלומדה עודכן.": "Guide name updated.",
  "פעולה בלתי הפיכה: הלומדה וכל שלביה, רכיביה וכללי ההתקדמות שלה יימחקו. שם לאישור:": "This action cannot be undone: the guide, its steps, elements and progression rules will be deleted. Name to confirm:",
  "שם הלומדה אינו תואם. המחיקה לא בוצעה.": "Guide name does not match. Nothing was deleted.",
  "מחיקה לצמיתות": "Permanently delete",
  "יש לבחור אפליקציה אחת.": "Select one application.",
  "האפליקציה שנבחרה אינה זמינה.": "The selected application is unavailable.",
  "האפליקציה נוספה.": "Application added.",
  "האפליקציה משויכת לשלבים בלומדה.": "This application is assigned to guide steps.",
  "האפליקציה הוסרה.": "Application removed.",
  "שגיאה לא ידועה": "Unknown error",
  "ה־Learner הופעל לבדיקת הלומדה. יש להכין את אפליקציית היעד.": "Learner started for guide testing. Prepare the target application.",
  "יש להוסיף אפליקציה ללומדה לפני יצירת שלב.": "Add an application to the guide before creating a step.",
  "לאחר יצירת השלב ניתן לבחור רכיב יעד ולהגדיר תנאי התקדמות.": "After creating the step, select a target element and configure progression.",
  "השלב נוצר. כרגע התקדמותו ידנית עד שיוגדרו רכיב יעד ותנאי התקדמות.": "Step created. Progression is manual until a target and progression condition are configured.",
  "משתנה לא קיים": "Variable not found",
  "פריט נתונים": "Data item",
  "פריט ברשימה": "List item",
  "שורה בטבלה": "Table row",
  "רכיב טקסט": "Text element",
  "לא נמצא מזהה רכיב שניתן לערוך.": "No editable element identifier found.",
  "מזהה הרכיב ארוך מדי.": "Element identifier is too long.",
  "ערך הרכיב עודכן.": "Element value updated.",
  "לשני משתנים יש אותו ערך מוצג. יש להבחין בין ערכיהם לפני העריכה.": "Two variables have the same displayed value. Distinguish them before editing.",
  "למשתנים שונים יש אותו ערך מוצג. יש להגדיר ערכים שונים לפני העריכה.": "Different variables have the same displayed value. Assign different values before editing.",
  "מקם את הסמן בטקסט ולחץ על הוסף משתנה. אפשר גם להחליף טקסט מסומן.": "Place the cursor in the text and click Insert variable. Selected text can also be replaced.",
  "סימון משתנה לא מוכר. יש להוסיף משתנים באמצעות הכפתור.": "Unknown variable token. Insert variables using the button.",
  "הטקסט ארוך מדי.": "Text is too long.",
  "לשני משתנים יש אותו ערך מוצג. יש להגדיר ערכים שונים לפני השילוב.": "Two variables have the same displayed value. Assign different values before combining them.",
  "יש לבחור את המשתנה בשלמותו או למקם את הסמן מחוץ לסימון.": "Select the entire variable or place the cursor outside the token.",
  "השינוי ישפיע על תוכן הבועית בלבד. רכיבי יעד וכללי התקדמות אינם משתנים.": "This change affects only bubble content. Target elements and progression rules are unchanged.",
  "פרטי השלב נשמרו.": "Step details saved.",
  "תוסף לא מחובר": "Extension disconnected",
  "התוסף אינו זמין": "Extension unavailable"
});
// Translate complete UI messages and their fixed prefixes while preserving variable values.
window.instructorT = function (message) {
  const source = String(message ?? "");
  if (instructorLanguage !== "en") return source;
  if (instructorLabels[source]) return instructorLabels[source];
  const prefix = Object.keys(instructorLabels)
    .filter(key => key.length >= 5 && source.startsWith(key))
    .sort((a, b) => b.length - a.length)[0];
  return prefix ? instructorLabels[prefix] + source.slice(prefix.length) : source;
};
Object.assign(instructorLabels, {
  "לחיצה על הרכיב": "Click the element",
  "הוזן ערך בשדה": "Field has a value",
  "השדה מכיל ערך מסוים": "Field contains a specific value",
  "מכיל את הערך": "Contains the value",
  "מופיע במסך": "Appears on screen",
  "המאפיין \"": "Attribute \"",
  "הנבחר": "selected",
  "\" שווה ל־\"": "\" equals \"",
  "הערך המלא": "Full value",
  "מספר בלבד": "Number only",
  "חילוץ מותאם אישית": "Custom extraction",
  "למחוק את שלב ": "Delete step ",
  "נמחק. סדר השלבים עודכן.": "deleted. Step order updated.",
  "ה־Learner הופעל משלב ": "Learner started at step ",
  ". הבועית תוצג כשהרכיב המתאים יהיה זמין.": ". The bubble will appear when the target element is available.",
  "הרכיב מופיע במסך": "Element appears on screen",
  "הרכיב מכיל ערך מסוים": "Element contains a specific value",
  "עריכת תנאי השלמה — שלב ": "Edit completion condition — Step ",
  "עריכת תנאי התקדמות — שלב ": "Edit progression condition — Step ",
  "עריכת רכיב במסך — שלב ": "Edit screen element — Step ",
  "זיהוי לפי תוכן": "Identify by content",
  "יישמר לדוגמה:": "Example saved value:",
  "השמירה נכשלה:": "Save failed:",
  "ערך התחלתי:": "Initial value:",
  "למחוק את המשתנה \"": "Delete variable \"",
  "הלומדה נוצרה עם ": "Guide created with ",
  " אפליקציות.": " applications.",
  "הלומדה \"": "Guide \"",
  "\" נמחקה.": "\" deleted.",
  "תוסף ה־Learner לא הצליח לתקשר עם Native Host:": "The Learner extension could not communicate with Native Host:",
  "לא התקבל אישור מתוסף ה־Learner. ודא שהתוסף GuideMe פעיל ב־Chrome ורענן את חלון ה־Instructor (Ctrl+R) לאחר עדכון התוסף.": "No response from the Learner extension. Ensure GuideMe is enabled in Chrome and refresh Instructor (Ctrl+R) after updating the extension.",
  "בחירת רכיב אחר": "Select another element",
  "עריכה": "Edit",
  "הסרה": "Remove",
  "תוסף מחובר": "Extension connected",
  "ערך צפוי": "Expected value",
  "שדה טקסט רב־שורות": "Multiline text field",
  "שדה קלט": "Input field",
  "רשימת בחירה": "Dropdown",
  "יצירת שלב": "Create step",
  "יצירת לומדה": "Create guide",
  "הוספת אפליקציה": "Add application",
  "שלב": "Step",
  "מושבת": "Disabled",
  "כותרת (H": "Heading (H"
});
Object.assign(instructorLabels, {
  "שגיאה בתקשורת עם השרת המקומי (": "Local server communication error (",
  "נמצאו כמה לשוניות מתאימות. הקלד את מספר הלשונית:\\n": "Multiple matching tabs found. Enter the tab number:\n",
  "לא ניתן להפעיל את ממפה הרכיבים:": "Unable to start element picker:",
  "האפליקציה משויכת לשלבים בלומדה": "The application is assigned to guide steps",
  "למחוק את שלב": "Delete step",
  "ה־Learner הופעל משלב": "Learner started at step",
  "עריכת תנאי השלמה — שלב": "Edit completion condition — Step",
  "עריכת תנאי התקדמות — שלב": "Edit progression condition — Step",
  "עריכת רכיב במסך — שלב": "Edit screen element — Step",
  "הלומדה נוצרה עם": "Guide created with",
  "הלומדה \"": "Guide \"",
  "ממפה פעיל: העבר עכבר מעל רכיב ולחץ Ctrl+Shift+Space לבחירה (Esc לביטול).": "Picker active: hover over an element and press Ctrl+Shift+Space to select (Esc to cancel)."
});
Object.assign(instructorLabels, {
  "סגירת הודעה": "Close notification",
  ", מושבת": ", disabled",
  "\n\nהשלב, הרכיבים וכללי ההתקדמות הייחודיים לו יימחקו לצמיתות.": "\n\nThe step, its elements, and its progression rules will be permanently deleted.",
  " — הערך המלא": " — full value",
  " — המספר מתוך הערך": " — number extracted from value",
  "האפליקציה משויכת לשלבים בלומדה": "The application is assigned to guide steps",
  " אפליקציות.": " applications.",
  "הקלד את שם הלומדה לאישור מחיקה": "Enter the guide name to confirm deletion",
  "מה להציג ללומד?": "What should the learner see?",
  "טקסט הבועית": "Bubble text"
});
Object.assign(instructorLabels, {
  "שלב ": "Step ",
  " מכיל את הערך": " contains the value",
  " נמחק. סדר השלבים עודכן.": " deleted. Step order updated.",
  "נמצאו כמה לשוניות מתאימות. הקלד את מספר הלשונית:\n": "Multiple matching tabs found. Enter the tab number:\n",
  "לא ניתן להפעיל את ממפה הרכיבים:": "Unable to start the element picker:",
  "יישמר לדוגמה:": "Example saved value:",
  "השמירה נכשלה:": "Save failed:",
  "ערך התחלתי:": "Initial value:",
  "פעולה בלתי הפיכה: הלומדה וכל שלביה, רכיביה וכללי ההתקדמות שלה יימחקו. שם לאישור:": "This cannot be undone: the guide and all its steps, elements and progression rules will be deleted. Name to confirm:",
  "תוסף ה־Learner לא הצליח לתקשר עם Native Host:": "The Learner extension could not communicate with Native Host:",
  "עריכת שלב ": "Edit step "
});
Object.assign(instructorLabels, {
  "מכיל את הערך ": "Contains the value ",
  " מכיל את הערך ": " contains the value ",
  "לא ניתן להפעיל את ממפה הרכיבים: ": "Unable to start the element picker: ",
  "יישמר לדוגמה: ": "Example saved value: ",
  "השמירה נכשלה: ": "Save failed: ",
  "ערך התחלתי: ": "Initial value: ",
  "פעולה בלתי הפיכה: הלומדה וכל שלביה, רכיביה וכללי ההתקדמות שלה יימחקו. שם לאישור: ": "This action cannot be undone: the guide, its steps, elements and progression rules will be deleted. Name to confirm: ",
  "תוסף ה־Learner לא הצליח לתקשר עם Native Host: ": "The Learner extension could not communicate with Native Host: "
});
const reverseInstructorLabels = Object.fromEntries(
  Object.entries(instructorLabels).map(([he, en]) => [en, he]));
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
