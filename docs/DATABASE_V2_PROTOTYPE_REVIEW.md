# GuideMe V2 — אב־טיפוס סכמת בסיס נתונים (16.10.2026)

**סטטוס: ניסוי ארכיטקטוני בלבד; לא מסד ייצור ולא חוזה Learner מאושר.**

נבדק עותק של `GuideMe-Test.db` (מסד בדיקות). עקרונות: מפתח מורכב להיררכיית בעלות, מזהים יציבים שאינם תלויים בסדר, הגדרות רכיב משותפות לתפקידי איתור/אימות/לכידה, והפרדת הגדרת רכיב מהקשרים המשתמשים בו.

## 12 הישויות באב־הטיפוס

| טבלה | מפתח ראשי | אחריות |
| --- | --- | --- |
| Guides | GuideId | זהות מדריך |
| GuideSteps | GuideId + StepId | זהות שלב; StepOrder נפרד |
| GuideApplicationContexts | GuideId + ContextId | הקשר אפליקציה |
| ApplicationContextMatchers | GuideId + ContextId + MatcherId | קריטריונים לאיתור האפליקציה; MatcherOrder נפרד |
| StepBubbles | GuideId + StepId | תוכן/מיקום בועית |
| StepTargets | GuideId + StepId + TargetId | זהות רכיב ותפקיד (Primary/Anchor/Completion/Capture) |
| TargetLocators | GuideId + StepId + TargetId + LocatorId | דרך זיהוי הרכיב |
| TargetAnchorRelations | GuideId + StepId + TargetId + AnchorId | יחס רכיב לעוגן; AnchorOrder נפרד |
| StepValidations | GuideId + StepId | תנאי האימות הראשי |
| StepCompletionConditions | GuideId + StepId + ConditionId | תנאי השלמה; ConditionOrder נפרד |
| GuideVariables | GuideId + VariableId | משתנים דינמיים בעלי זהות |
| StepCaptures | GuideId + StepId + CaptureId | הגדרת לכידה המקושרת לרכיב ומשתנה |

## ממצאי הבדיקה הראשונית

אב־טיפוס SQLite מקומי נבנה **מבלי לשנות את מסד המקור**, עם 3 מדריכים, 116 שלבים, 91 תנאי השלמה, 21 יחסי עוגנים, 2 לכידות, 4 הקשרי אפליקציה ו־4 מאתרים של הקשרים. `PRAGMA integrity_check` החזיר `ok`; `PRAGMA foreign_key_check` לא החזיר הפרות.

**מגבלת התאימות:** הניסוי משמר את רוב נתוני ההרצה, אבל זו עדיין **לא מיגרציה מאומתת מבחינה התנהגותית**: יש לבחון טוקנים של משתנים ותזמון, סוגי Runtime בתוך JSON, עוגנים מקוננים, פירוש תנאי האימות והשפעת שינויי זהות על טעינה ושמירה. ל־Learner הקיים אין כיום מתאם לסכמה הזו. אין להריץ אותו מול אב־הטיפוס.

## החלטות שאינן סגורות

1. לקבוע האם מזהה מדריך הוא המספרי `GuideId` או `Guides.Key` הלוגי.
2. לקבוע האם יעדים צריכים להיות ברמת שלב או ברמת מדריך לשימוש חוזר אמיתי; כרגע האב־טיפוס נותן אחידות **בתוך שלב בלבד**.
3. להכריע על דרך חלופית ל-`FrameContextJson` ו-`OptionsJson` בלי לאבד יכולת ביטוי.
4. להבטיח כללים להוספה/הזזה/מחיקה של שלבים ולבצע בדיקת תלויות סמנטיות.
5. לתכנן תצוגות קריאות לעריכה, גרסאות מדריך ומנגנון פרסום, בטרם הקפאת הסכמה.
6. להחליט האם `Validation` הראשי ו-`CompletionConditions` יהיו ישות אחת או מופרדות, לפי משמעותן בפועל במנוע.
7. להגדיר מהי זהות יציבה של תנאי/עוגן וכיצד נשמרת בעת שינוי הסדר.

**אין לבצע שינויים במסד הפעיל, בייצור או ב-Learner לפני אישור מפורש ותוכנית אימות נתונים מלאה.**
