# GuideMe Instructor Web — הרצה ומצב מימוש

## הרצת הממשק החדש (Windows)

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull --ff-only origin main
dotnet build src\GuideMe.Instructor.Web\GuideMe.Instructor.Web.csproj
dotnet run --project src\GuideMe.Instructor.Web\GuideMe.Instructor.Web.csproj
```

הממשק נפתח בכתובת `http://127.0.0.1:53922` בדפדפן המקומי. אין צורך בשרת SampleApp ואין שינוי בפורטים של Learner.

## היכולות שכבר חוברו ל־V5

- הצגת רשימת לומדות וחיפוש.
- טעינת לומדה קיימת ורשימת השלבים.
- הצגת נתוני השלבים, רכיבי היעד והכללים לקריאה.
- יצירת לומדה עם הקשר אפליקציה ראשון (Web לפי UrlHost, Windows לפי ProcessName).
- שינוי שם לומדה.
- הוספת הקשר אפליקציה, שינוי שם תצוגה, החלפת זיהוי והסרת הקשר שאינו בשימוש.

המערכת משתמשת ישירות ב־`V5GuideReader` וב־`V5GuideWriter` הקיימים, ובמסד `C:\ProgramData\GuideMe\Data\GuideMe-V5.db`. המערכת אינה יוצרת או ממירה DB.

## גבולות הגרסה

- עריכת פרטי השלבים, בחירת חלון יעד אינטראקטיבית והקלטה בפועל טרם הועברו מהממשק הישן.
- ה־WPF הישן נשאר זמין כדי למנוע אובדן יכולות במהלך המעבר.
- התוסף הישן של Instructor ומנגנון BrowserCaptureServer טרם חוברו ל־Web Host החדש.
- אין לטעון שה־Web Instructor מחליף כבר את כלל היכולות של WPF; זוהי גרסת ניהול ראשונית.
- אין לפתוח פורט לרשת. השרת מאזין רק ב־127.0.0.1 ומחייב אסימון סשן לקריאות API; פעולות שינוי דורשות JSON ובדיקת מקור. נדרשות בדיקות אבטחה לפני ייצור.
- לא בוצע Build על Windows מתוך סביבת העריכה. יש להריץ ולוודא מקומית.

## המשך

השלב הבא הוא להעביר לכידת Web ו־Windows לחוזי Host מקומיים ומצבי הקלטה, תוך שמירה על הפרדה מלאה מתוסף Learner. לאחר מכן עריכת שלבים מלאה ובדיקות יצירה־הרצה מול Learner.
