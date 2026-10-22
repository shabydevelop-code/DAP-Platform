# GuideMe Instructor Web UI — תשתיות משותפות עם Learner

## החלטה

ה-Instructor מיועד לפעול במחשב Windows אחד. הממשק המיועד יהיה Web מקומי, אך זיהוי ולכידת אפליקציות Windows ימשיכו לפעול ברכיב .NET מקומי באותו סשן משתמש. אין להקים שירות ענן או לחשוף את API הניהול לרשת.

מטרת המסמך: בדיקת שימוש חוזר בתשתיות Learner קיימות, לפני מימוש Web UI חדש. אין במסמך הצהרה ש-Web UI או Windows Capture כבר מומשו.

## ממצאים מאומתים בקוד

| תחום | קוד קיים | מסקנה |
|---|---|---|
| גישור Web ב-Learner | `src/GuideMe.Learner.ChromeExtension/service-worker.js` משתמש ב-`chrome.runtime.connectNative("com.guideme.web_runtime")` | להשתמש בתבנית Native Messaging עבור התוסף, במקום לפתוח API Windows מתוך דף רגיל |
| Native Host | `src/GuideMe.Runtime.Web.NativeHost/Program.cs` מעביר הודעות בתקן Chrome Native Messaging (אורך + JSON) | שימוש חוזר בעקרונות פרוטוקול ומחזור חיי חיבור; לא לחבר Instructor לפקודות Learner ללא הפרדת הרשאות |
| IPC מקומי | אותו Native Host משתמש ב-`NamedPipeClientStream` ובשם Pipe הכולל `Process.SessionId` | תבנית שימושית לתקשורת בין תהליכים; שם סשן אינו אימות זהות או הרשאת פעולה |
| Windows | `src/GuideMe.Runtime.Windows/Targets/WindowsTargetResolver.cs`, `src/GuideMe.Runtime.Windows/Learner/WindowsGuideRuntime.cs` | לשתף חוזים וכלי זיהוי UI Automation; לא להשתמש ברכיב הפעלת לומדה כאילו הוא רכיב לכידה |
| Instructor כיום | `src/GuideMe.Instructor/BrowserCaptureServer.cs` מאזין ל-`http://127.0.0.1:53921/` ומתבסס על אסימון; `src/GuideMe.Instructor.ChromeExtension/service-worker.js` פונה אליו | תשתית Instructor נפרדת; אין לטעון שקיימת כבר שכבת תקשורת משותפת |
| פרסום | `scripts/Publish-GuideMe.ps1` מפרסם Native Host ותוסף Learner ל-`C:\GuideMe` | שינוי תשתית מחייב התאמה מפורשת של פרסום והתקנה, בלי לשבור Learner |

## גבולות ושיתוף

1. **משותף:** זיהוי ApplicationContext; אבסטרקציות של בחירת יעד; API לקריאת מאפייני UI Automation; חוזי הודעות מאומתים; הגדרות סשן, ביטול פעולות ולוגיקה להצמדת חלון.
2. **נפרד:** פקודות של Learner (ליווי/הרצה/בועיות) מול פקודות Instructor (בחירת יעד, לכידה, כתיבת לומדה). אין לאפשר לתוסף או לאתר לגשת לפעולות כתיבה ללא הרשאה מפורשת.
3. **חלוקת אחריות:** דף Web מנהל UI; Host מקומי מנהל DB, הרשאות ופעולות Windows; תוסף Chrome מספק גישה מורשית ל-DOM; אין להעביר יכולות UI Automation לקוד JavaScript בדפדפן.
4. **Windows Agent:** במחשב בודד אפשר לממש את יכולות Windows באותו תהליך Host, ללא Named Pipe נוסף. Named Pipe מתאים רק אם מפרידים לתהליכים, עם ACL למשתמש ובדיקת זהות לקוח.
5. **DB:** לשמור על `C:\ProgramData\GuideMe\Data\GuideMe-V5.db`; אין שינוי סכימה אוטומטי כחלק מהעברת ה-GUI.

## אבטחת מידע — תנאי סף לפני מעבר ל-Web UI

- Web UI מקומי בלבד; Bind ל-`127.0.0.1`. אין משמעות הדבר שמקור HTTP מקומי מהימן אוטומטית.
- כל קריאת API המשנה מידע או מפעילה יכולת Windows תדרוש אימות סשן, בדיקת Origin/Host, הגנת CSRF והרשאה לפי פעולה. לא להסתמך על CORS לבדו.
- יש לבחון מחדש את `/pair` ב-`BrowserCaptureServer`: בקוד הנוכחי הוא מחזיר Session Token למקור תוסף מותר על סמך Origin; זה אינו מנגנון אימות חזק. בעת איחוד יש לבטל או להקשיח מסלול bootstrap זה.
- Native Messaging: לרשום Manifest עם `allowed_origins` מוגבל, לבדוק סוג/גודל הודעה ולהפריד תהליכי Instructor ו-Learner ברמת capability.
- Named Pipes: שם המשלב SessionId אינו מחליף ACL, impersonation או אימות זהות הדובר. לוודא הרשאות בפועל לפני שימוש חוזר.
- להימנע מ-Host בעל הרשאות Administrator; להפעיל בסשן המשתמש, לרבות Citrix/RDS אם יידרש בעתיד.
- לא לאפשר API כללי להרצת פקודות מערכת, PowerShell או JavaScript שרירותי מתוך UI הניהול.

## תוכנית ביצוע מומלצת

1. **מיפוי מלא של תשתיות IPC:** לאתר את שרת Named Pipe ב-Learner, את ACL שלו, את רשימת הפקודות ואת מנגנון האימות. מסמך זה מאמת את צד הלקוח ב-Native Host, לא את צד שרת ה-Pipe.
2. **חוזים משותפים ומוגבלים:** להגדיר DTO לפעולות `IdentifyApplication`, `SelectTarget`, `InspectElement`, `BeginCapture`, `EndCapture`, בלי לחשוף API שרירותי.
3. **Host מקומי:** לממש API מינימלי עם הרשאות ב-.NET, לשמור על V5 ולשלב Windows UI Automation מאותו סשן.
4. **Web UI:** להעביר בהדרגה מסכים מ-WPF ולוודא פריטי ניהול, הזדהות ויכולת עבודה ללא חיבור לאינטרנט.
5. **תוסף:** להשאיר Learner production מבודד; לבצע מיגרציה מבוקרת מתוסף Instructor הקיים רק אחרי בדיקות אבטחה ותאימות.
6. **רגרסיה:** להפעיל Web, Windows ומשולב ב-Learner לצד יצירה ועריכה ב-Instructor; לאשר שאין התנגשות סשנים/פורטים ושאין שינוי בהתנהגות Learner.

## מה לא נבדק עדיין

- הרשאות גישה בפועל לשרת Named Pipe ול-Native Host manifest המותקן.
- מימוש Windows capture מלא בצד Instructor.
- מבחני אבטחה של API מקומי, כולל אתרי צד שלישי, CSRF, DNS rebinding ו-cross-user isolation.

אין לשנות את Learner הפעיל לפני שבדיקות אלו הושלמו.
