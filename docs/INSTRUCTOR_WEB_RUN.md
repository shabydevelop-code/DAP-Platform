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

- עריכת פרטי השלבים ושמירת הקלטה כשלב V5 עדיין לא הועברו. הקלטת Web ראשונית קיימת לסקירה בלבד.
- פרויקט WPF הישן הוסר; רכיבי V5 נמצאים כעת בתוך פרויקט Web Instructor.
- התוסף משתמש ב־Native Messaging ייעודי ומעביר את האירועים דרך Named Pipe אל Web Instructor.
- אין לטעון שה־Web Instructor מחליף כבר את כלל היכולות של WPF; זוהי גרסת ניהול ראשונית.
- אין לפתוח פורט לרשת. השרת מאזין רק ב־127.0.0.1 ומחייב אסימון סשן לקריאות API; פעולות שינוי דורשות JSON ובדיקת מקור. נדרשות בדיקות אבטחה לפני ייצור.
- לא בוצע Build על Windows מתוך סביבת העריכה. יש להריץ ולוודא מקומית.

## המשך

השלב הבא הוא להעביר לכידת Web ו־Windows לחוזי Host מקומיים ומצבי הקלטה, תוך שמירה על הפרדה מלאה מתוסף Learner. לאחר מכן עריכת שלבים מלאה ובדיקות יצירה־הרצה מול Learner.


## תוסף Instructor ללא ממשק משתמש — Native Messaging

ה־Instructor משתמש ב־Native Messaging נפרד מה־Learner:
- Host בשם `com.guideme.instructor` מפורסם תחת `C:\\GuideMe\\InstructorWeb\\NativeHost`.
- רישום Chrome נכתב ל־`HKCU\\Software\\Google\\Chrome\\NativeMessagingHosts\\com.guideme.instructor`.
- קובץ ה־Manifest נוצר תחת `%LOCALAPPDATA%\\GuideMe\\NativeMessaging`.
- ה־Host מתקשר עם Web Instructor דרך Named Pipe המוגבל למשתמש הנוכחי ול־Windows Session.
- התוסף אינו פונה אל HTTP ואינו מבקש קוד צימוד. פורט `53922` משמש לממשק הניהול המקומי בלבד.
- מנגנון ה־Learner וה־Host שלו אינם משתנים.

### פרסום והפעלה

```powershell
cd C:\\yossi\\ChatGpt\\DAP-Platform
git pull --ff-only origin main
.\\scripts\\Publish-GuideMeInstructorWeb.ps1
& "C:\\GuideMe\\InstructorWeb\\GuideMe.Instructor.Web.exe"
```

בדפדפן: `chrome://extensions` → GuideMe Instructor → Reload. מקור התוסף חייב להיות `C:\\GuideMe\\InstructorChromeExtension`. גרסת התוסף `0.4.0`.

### בדיקת הקלטה

1. פתח את Web Instructor בכתובת `http://127.0.0.1:53922`.
2. ודא שחיווי **תוסף מחובר** מופיע בכותרת העליונה. ההתחברות מתבצעת אוטומטית ללא צימוד או כפתור חיבור.
3. פתח לומדה ובחר שלב המשויך לאפליקציית Web. אזור ההקלטה מופיע רק במסך השלבים, עבור השלב שנבחר. האפליקציה המשויכת נבחרת מהנתונים השמורים של השלב.
4. לחץ **זהה אפליקציה**, ולאחר זיהוי **התחל הקלטה**. בצע פעולה באתר ולחץ **עצור הקלטה**.

הקלטת Web מציגה אירועים בזיכרון בלבד ואינה משנה את השלב הנבחר; שמירה כשלבי V5 עדיין לא מומשה. הקלטת Windows עדיין לא חוברה. יש לבצע Build ורגרסיה מקומית ב־Windows.

## אייקון Instructor

הפרסום משתמש בקובץ האייקון המקורי שנמסר על ידי המשתמש (`src/GuideMe.Instructor.ChromeExtension/instructor-source.png`), ללא ציור חדש. נוצרות גרסאות 16, 48 ו־128 פיקסלים.

## מחיקת לומדה קיימת

במסך פרטי לומדה קיים כפתור **מחיקת לומדה**. כדי לאשר מחיקה יש להקליד את שם הלומדה במלואו. ה־API מאמת את השם מול הנתון העדכני במסד הנתונים, ורק אז מפעיל את `V5GuideWriter.DeleteGuide` הקיים מה־Instructor הישן. הפעולה מתבצעת בטרנזקציה אחת ומוחקת את ישויות הלומדה בטבלאות V5 לפי סדר מפתחות זרים. היא אינה משנה לומדות אחרות. המחיקה בלתי הפיכה; יש לבדוק תחילה עם לומדת ניסוי בלבד, ולא עם לומדות Web/Windows המאומתות.
