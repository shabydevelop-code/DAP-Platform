# GuideMe V4 — מפענוח טכני לתיאור פעולה קריא

**סטטוס:** אב־טיפוס ניסיוני בלבד. אין שינוי ב־GuideMe.db הפעיל או ב־Learner.

## מה נבנה

נוצר מסד ניסיוני חדש `GuideMe-V4-Prototype.db` המבוסס על עותק `GuideMe-V3-Prototype.db`, עם שלוש טבלאות נוספות ותצוגה לקריאה:

| טבלה | מפתח ראשי | משמעות |
|---|---|---|
| StepExpectedOutcomes | GuideId + StepId | תוצאה צפויה, תיאור ומצב אישור |
| StepAdvancePolicies | GuideId + StepId | מתי המדריך מתקדם |
| StepObservedSignals | GuideId + StepId + SignalId | מה ה־Learner בודק בפועל, לרבות מקור הכלל |
| ReadableStepSemantics (VIEW) | — | תצוגה משולבת של הוראת המשתמש, פעולה, תוצאה, אימות והתקדמות |

זהות `StepId` נשארת יציבה ונפרדת מ־`StepOrder`; גם `SignalId` נפרד מ־`SignalOrder`.

## תוצאות בדיקת אב־הטיפוס

- 3 מדריכים, 116 שלבים.
- 116 רשומות `StepAdvancePolicies` הועברו מ־`AdvanceMode` הקיים.
- 116 רשומות `StepExpectedOutcomes`: כולן `Unspecified` ומסומנות `RequiresAuthorConfirmation`, כי אין ראיה מספקת לתוצאה העסקית מתוך מודל המקור.
- 203 רשומות `StepObservedSignals`: 112 כללי אימות ראשיים ו־91 תנאי השלמה נוספים. לכל כלל `SignalRole` ו־`DefinitionSource` מפורשים.
- 116 `StepInteractions` נשארו `Unspecified` ללא המצאת סוג פעולה.
- `PRAGMA integrity_check` = `ok`; `PRAGMA foreign_key_check` = אפס הפרות.

## הסמנטיקה שחשוב לא לערבב

1. **RequiredInteraction**: מה המשתמש אמור לעשות. עדיין חסר במקור, ויש לאשר אותו מתוך תהליך יצירת המדריך.
2. **ExpectedOutcome**: מה אמור להיות מצב המסך/התהליך לאחר הפעולה. אין לזהות אוטומטית כל כלל `target-exists` עם תוצאה עסקית.
3. **ObservedSignal**: עובדה מוגדרת שהמנוע יודע לבדוק (`clicked`, `value-equals`, `target-exists` וכו'). הפרדה בין `PrimaryValidation` ו־`AdditionalCompletionCondition` שומרת את משמעות המקור עד לבדיקת חוקי הריצה המלאים.
4. **AdvancePolicy**: `AutomaticOnValidation` או `Manual`, ללא שינוי ערכי המקור.
5. **Source/Confidence**: מידע שלא ניתן להסיק ללא אישור נשמר כבלתי מוגדר. אין למלא בהשערות מתוך `BubbleContent`, `Locator` או `ValidationKind`.

## קריאות

התצוגה `ReadableStepSemantics` מחברת מזהה מדריך, שלב, סדר, הוראת המשתמש, פעולה, סטטוס אישור, תוצאה, מדיניות התקדמות, ואוסף אותות האימות. זו תצוגת עזר; הנתונים הנורמטיביים נמצאים בטבלאות המקושרות במפתחות מורכבים.

## מגבלות

- זהו **שינוי מצטבר**, ולכן `AdvanceMode` וגם `StepAdvancePolicies` קיימים בניסוי במקביל, וכך גם טבלאות האימות הקודמות ו־`StepObservedSignals`. זו כפילות זמנית לניסוי; **לא** סכמה סופית.
- אין עדיין ניתוח מוכח של הלוגיקה המשלבת Validation עם CompletionConditions בכל אחד ממנועי Web/Windows. לא לאחד או לשנות התנהגות בלי בדיקה.
- עדיין אין הגדרת פעולה ותוצאה מאושרות, ואין מתאם Learner למסד החדש.
- נדרשים בדיקות round-trip, שינויי סדר, קשרי משתנים ורגרסיה של שלושת המדריכים לפני אימוץ.

## קבצי אב־הטיפוס שנוצרו מחוץ ל־GitHub

`/mnt/data/GuideMe-V4-Prototype.db`  
`/mnt/data/guideme-design/GuideMe-V4-additions.sql`  
`/mnt/data/guideme-design/build_v4.py`

שורות אלה הן מידע על קבצי ניסוי בשיחה, ואינן טוענות שהם הועלו למאגר.

## המשך

לבחון ישירות את שילוב האימות וההתקדמות בקוד ה־Web/Windows. לאחר מכן לקבוע סכמה נורמטיבית אחת לכל מושג, ללא כפילויות זמניות, ולבצע מבחן עריכה/שמירה של שלב תוך שימור הזהויות.
