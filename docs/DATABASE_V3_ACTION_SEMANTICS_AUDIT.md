# GuideMe V3 — מיפוי בין נתוני הריצה למשמעות הפעולה

**סטטוס:** דוח בדיקת נתונים וקוד; אין שינוי ב-DB הפעיל, בקוד ה-Learner או במסד האב־טיפוס. נבדק קובץ הניסוי `GuideMe-V3-Prototype.db` מול הגדרות `GuideStep` ו-`ValidationDefinition`.

## ממצאים מספריים

| הגדרה | רשומות |
| --- | ---: |
| שלבים | 116 |
| StepInteractions מסוג Unspecified | 116 |
| StepValidations | 112 |
| Validation: clicked | 65 |
| Validation: value-equals | 26 |
| Validation: target-disappeared | 11 |
| Validation: value-not-empty | 10 |
| StepCompletionConditions | 91 |
| Completion: target-exists | 81 |
| Completion: target-not-exists | 6 |
| Completion: target-replaced | 2 |
| Completion: target-enabled | 2 |
| מדיניות AutomaticOnValidation | 112 |
| מדיניות Manual | 4 |
| שימושים: Primary/Completion/Anchor/Capture | 112 / 91 / 21 / 2 |

**כלל מחייב:** ערך `ValidationKind='clicked'` הוא ראיה ל*כלל האימות הקיים*; הוא אינו מוכיח בפני עצמו מה רצף הפעולות העסקי שהעורך רצה להגדיר. בדומה, `value-equals` לא מוכיח לבדו אם המשתמש מקליד, בוחר, או רק מתבקש לוודא ערך. אסור למלא `InteractionKind` על סמך הסקה כזו באופן אוטומטי.

## חוזי משמעות — מה צריך לשמור בנפרד

**פעולה צפויה:** מזהה פעולה טיפוסי (`InteractionKind`), רכיב רלוונטי, פרמטרים, ולעיתים תנאי מקדים; `Unspecified` הוא ערך מותר בשלבי הסבה בלבד, לא יעד מוצר סופי.

**אירוע שנצפה:** פעולה או שינוי מצב שה-Learner מסוגל לזהות בפועל, למשל `clicked`, `value-equals`, `target-disappeared`; זהו המובן של שדות אימות קיימים, ואין להחליפו ב"פעולה צפויה" ללא ראיות.

**תוצאה צפויה:** מצב התצוגה/התהליך שאמור להתקיים אחרי הפעולה, כולל רכיב או ערך, והקשר אפליקציה.

**תנאי השלמה:** תנאי לוגי שהמערכת בודקת; הוא עשוי לשלב כמה תצפיות, וחייב להגדיר באופן מפורש האם נדרש AND/OR, תזמון או סדר. אין להמציא יחס לוגי בלי לבדוק את מנועי Windows/Web.

**מדיניות מעבר:** `AutomaticOnValidation` לעומת `Manual`; מדיניות זו לא מגדירה בעצמה פעולה, גם כשהשם כולל Validation.

## דוגמת קריאה נכונה ב-DB

| היבט | ערך אפשרי מהמקור | האם מוכח? |
| --- | --- | --- |
| InstructionText | הצג פעילויות נוספות | כן, כטקסט הוראה |
| InteractionKind | Unspecified | כן — לא מוגדר במקור |
| ValidationKind | clicked | רק כאשר רשום כך בפועל |
| ExpectedOutcome | לא מוגדר | אין להמציא |
| AdvanceMode | AutomaticOnValidation | כאשר רשום כך בפועל |

כלומר: **אדם שפותח את ה-DB חייב לדעת גם מה ידוע, גם מה לא הוגדר, ומה מקור כל מסקנה.**

## בדיקות שנדרשות לפני מילוי סוגי פעולה

1. למפות במדויק את ההתנהגות של ערכי `ValidationKind` ואת שילובם עם `CompletionConditions` בכל מנוע ריצה.
2. להגדיר מילון `InteractionKind` כללי ומאומת (למשל Click, EnterText, SelectValue, Observe) בלי להסיק אותו רק מטקסט הבועית.
3. לאפשר ל-Instructor או לעורך לאשר פעולה ולהצמיד מקור: `AuthorConfirmed`, `MachineSuggested` או `LegacyUnspecified`.
4. להתאים את `ReadableSteps` כך שיוצגו בנפרד *פעולה*, *תצפית אימות*, *תוצאה צפויה*, *מדיניות מעבר* ו*רמת ודאות*.
5. לשמר את 116 השלבים במבחן round-trip ולא להכריז על תמיכה בהרצה עד בדיקות Web/Windows/Mixed.

## מסקנה

המסד החדש יודע לשמור ולהציג **איך מנגנון הריצה מאמת את מרבית השלבים**, אבל עדיין אינו יודע לתאר במפורש **מה האדם אמור לעשות** בכל שלב. זהו פער מידע אמיתי במקור, לא פגם שאפשר לתקן באמצעות שינוי שם עמודה. נדרשת יצירה או אישור של הגדרות הפעולה לפני שניתן להכריז שה-DB מאפשר למנגנון גנרי לשחזר פעולות באופן אמין.
