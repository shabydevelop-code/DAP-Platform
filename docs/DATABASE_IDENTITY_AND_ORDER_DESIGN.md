# GuideMe — מודל זהויות וסדר (תכנון מוצע, לא ממומש)

סטטוס: **הצעת ארכיטקטורה לבחינה**. אין לשנות את בסיס הנתונים הפעיל או את חוזי ה-Learner על סמך מסמך זה בלבד.

## העיקרון

**זהות ובעלות אינן סדר.** במודל העתידי, ישויות שבבעלות מדריך או שלב מזוהות במפתח מורכב המתאר את ההיררכיה שלהן. שדות כגון `StepOrder`, `ConditionOrder` ו-`AnchorOrder` משמשים לסידור בלבד ואינם חלק ממפתח ראשי אם העורך רשאי לשנות את הסדר.

מפתח ראשי מורכב הוא עדיין ייחודי — הייחודיות חלה על צירוף העמודות. אין חובה שכל עמודה בו תהיה ייחודית בפני עצמה.

## זהויות מוצעות

| ישות | מפתח ראשי לוגי מוצע | שדה סדר נפרד |
|---|---|---|
| Guide | GuideId | — |
| GuideStep | (GuideId, StepId) | StepOrder |
| StepCompletionCondition | (GuideId, StepId, ConditionId) | ConditionOrder |
| StepTarget (אם תוגדר ישות כזו) | (GuideId, StepId, TargetId) או זהות משותפת אחרת לפי בעלות אמיתית | — |
| TargetAnchor | (GuideId, StepId, TargetId, AnchorId) בהנחת בעלות של יעד | AnchorOrder |
| StepCapture | (GuideId, StepId, CaptureId) | CaptureOrder רק אם נדרש |
| GuideApplicationContext | (GuideId, ContextId) | — |
| ApplicationContextMatcher | (GuideId, ContextId, MatcherId) | MatcherOrder |

`StepId` כאן הוא מזהה יציב *בתוך המדריך*, לאו דווקא המספר הגלובלי הקיים ב-`GuideSteps.Id`. ניתן לשקול שימוש ב-`GuideSteps.Key` שכבר קיים כמזהה יציב, אך לא לקבל החלטת טיפוס/שם בטרם בדיקת כל ההפניות. `GuideId` יכול להישאר מספרי או להתבסס על `Guides.Key`; יש להכריע בעקביות לכל המודל.

## דוגמת DDL להמחשת האילוצים — לא סקריפט מיגרציה

```sql
CREATE TABLE GuideSteps (
    GuideId   INTEGER NOT NULL,
    StepId    TEXT NOT NULL,
    StepOrder INTEGER NOT NULL,
    Name      TEXT,
    PRIMARY KEY (GuideId, StepId),
    UNIQUE (GuideId, StepOrder),
    FOREIGN KEY (GuideId) REFERENCES Guides(Id)
);

CREATE TABLE StepCompletionConditions (
    GuideId        INTEGER NOT NULL,
    StepId         TEXT NOT NULL,
    ConditionId    TEXT NOT NULL,
    ConditionOrder INTEGER NOT NULL,
    Kind           TEXT NOT NULL,
    PRIMARY KEY (GuideId, StepId, ConditionId),
    UNIQUE (GuideId, StepId, ConditionOrder),
    FOREIGN KEY (GuideId, StepId)
        REFERENCES GuideSteps (GuideId, StepId)
        ON DELETE CASCADE
);
```

יש לאכוף `PRAGMA foreign_keys=ON` בכל חיבור SQLite. הדוגמה אינה מכסה את כל עמודות ההרצה, שדות ה-JSON, פעולות או יעדים. לא להריץ על מסד קיים.

## שינוי סדר

1. הזזת שלב משנה רק `StepOrder`; `GuideId` ו-`StepId` אינם משתנים.
2. גם תנאים ועוגנים יכולים לקבל מזהי זהות יציבים ושדות סדר עצמאיים.
3. אילוץ `UNIQUE (GuideId, StepOrder)` מגן מכפילויות, אך החלפת סדר של שני שלבים חייבת להתבצע בטרנזקציה ובאלגוריתם בטוח לאילוצי UNIQUE של SQLite (למשל שימוש בערכי סדר זמניים ייחודיים, או יצירת סדר מחדש בטבלה מתאימה); לא להניח שעדכון שתי שורות בהחלפה ישירה יצליח.
4. שינוי סדר מחייב בנוסף אימות **תלויות סמנטיות**: שימוש במשתנה לפני לכידתו, מעבר בין הקשרי Web/Windows, כללי ניווט ותנאי מעבר. תקינות FK לבדה אינה מספיקה.
5. מפתחות שמזהים ישויות משותפות בין שלבים צריכים לשקף את בעלותן האמיתית (מדריך/אפליקציה/מאגר רכיבים) ולא לכפות `StepId`.

## מה נבדק בקוד הקיים

`SqliteGuideStepRepository.SaveStepAsync` משתמש כבר ב-`ON CONFLICT(GuideId,Key)`, ואילו ההפניות מ-`TargetAnchors`, `StepCaptures` ו-`StepCompletionConditions` משתמשות במזהה המספרי `GuideStepId`. כלומר יש כיום מפתח לוגי יציב לשלב, אך ההיררכיה אינה מוחלת באופן אחיד על קשרי טבלאות המשנה.

`ReplaceStepsAsync` מוחק את שלבי המדריך ומכניס אותם מחדש. התנהגות זו ראויה לבחינה מול דרישת הזהויות היציבות, ובמיוחד אם יהיו הפניות בין שלבים או גרסאות עתידיות. אין להסיק מהמסמך שהיא כבר תוקנה.

## שערי קבלה לפני שינוי DB או Learner

- למפות **את כל** הטבלאות, האינדקסים, ה-FK, ה-UNIQUE, ה-JSON והקריאות בקוד אל המודל העתידי.
- להכריע בזהות הלוגית של כל ישות ובעלותה; לבטל סדר כחלק מזהות בכל אוסף שניתן לסדר.
- לבדוק תרחישים: הזזת שלב, הכנסת שלב באמצע, שכפול, מחיקה, העברה בין מדריכים, שינוי סדר עוגנים ותנאים, שימוש בערך שנלכד בשלבים אחרים.
- להבטיח יכולת טעינה, עריכה ושמירה חוזרת ב-Instructor, כולל עקביות מפתחות והצגה אנושית.
- להכין סכמה מלאה ומבחן המרה על **עותק** של `docs/sample-data/GuideMe-Test.db`, עם השוואת כל המדריכים והשלבים לפני/אחרי.
- לבצע מיגרציה בפועל רק לאחר אישור נפרד, תוך שמירה על מסד הנתונים הפעיל.

## גבולות המסמך

זהו תכנון **מודל המפתחות והסדר**, לא אישור לסכמת בסיס הנתונים המלאה. עדיין צריך לבחון מחדש את פירוק האחריות של `GuideSteps`, ייצוג רכיבים, JSON, תנאי השלמה, לכידות והקשרים — כדי לא לקבל רק אותן טבלאות בשמות אחרים.
