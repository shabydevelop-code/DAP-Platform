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

## תוסף Chrome נפרד — Web Instructor

תוסף Instructor גרסה 0.2.0 מתקשר ל-`http://127.0.0.1:53922/capture/*` בלבד (ולא לפורט 53921).
אין לשנות את תוסף Learner.

להתקנת גרסת Instructor מפורסמת:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull --ff-only origin main
dotnet build src\GuideMe.Instructor.Web\GuideMe.Instructor.Web.csproj
.\scripts\Publish-GuideMeInstructorWeb.ps1
& "C:\GuideMe\InstructorWeb\GuideMe.Instructor.Web.exe"
```

Chrome → `chrome://extensions` → Developer mode → Load unpacked:
`C:\GuideMe\InstructorChromeExtension`.
אם התוסף כבר טעון מאותה תיקייה, מספיק `Reload`. אם הוא נטען קודם מתיקיית המקור, יש לעדכן את מיקום התוסף או לבצע Reload של המקור המעודכן. אין להשתמש בתוסף Learner.

### תהליך חיבור והקלטה ראשונית

1. פתח את Web Instructor ב-`http://127.0.0.1:53922`.
2. לחץ **אפשר חיבור לתוסף**. נפתח חלון הרשאה קצר ל-30 שניות.
3. עבור ללשונית אתר היעד, פתח את תוסף **GuideMe Instructor** ולחץ **בחר לשונית זו כיעד**.
4. חזור ל-Instructor ובדוק חיווי **מחובר** והצגת כתובת היעד.
5. לחץ **התחל הקלטה**, בצע פעולה באתר היעד, לחץ **עצור הקלטה**.
6. אירועי הפעולה יוצגו לסקירה. **האירועים אינם נשמרים עדיין ללומדה או למסד V5**.

### גבולות ואבטחה

- לא נדרש ולא נרשם Native Host חדש; בגרסה זו ערוץ Instructor משתמש ב-HTTP loopback נפרד מה-Learner.
- השרת בודק Origin של התוסף לפי מזהה שמתקבל מהמפתח הקבוע ב-manifest. ה-API של הממשק מחייב אסימון סשן.
- שיוך תוסף מתאפשר רק בחלון הרשאה מפורש של 30 שניות, וסוגי האירועים שמורים בסשן מקומי.
- החיבור קולט רק פעולות Web. בחירת רכיב Windows והקלטה אליו טרם הועברו.
- יש להשאיר את Instructor WPF הקיים ללא שינוי כרגע. הוא עדיין משתמש בערוץ ישן ב-53921; התוסף החדש מכוון ל-Web Instructor בלבד ולכן אינו מתאים להרצה מול WPF הישן.
- יש לאמת בנייה, חיבור תוסף והקלטה ב-Windows לפני הכרזת רגרסיה תקינה.
