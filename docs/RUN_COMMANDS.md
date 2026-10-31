# פקודות הרצה מאומתות — GuideMe / SampleApp

> **כלל:** הפרמטר `--guide` מקבל את **Key** של המדריך, לא את השדה המספרי `Guides.Id`. אין להשתמש ב-`--guide 965`.

## 1. הכנה: עדכון ופרסום GuideMe

יש לסגור את GuideMe ואת Native Host לפני פרסום לתיקיית ההתקנה. הפקודות מתוך PowerShell:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull --ff-only origin main
.\scripts\Publish-GuideMe.ps1
```

**סדר הרגרסיה:** עדכון → בנייה ופרסום → הפעלת אפליקציות הדוגמה → הפעלת מדריך מתוך `C:\GuideMe` → בדיקת תקינות.

## 2. מדריך Windows

בחלון PowerShell ראשון, הפעלת אפליקציית הדוגמה:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Windows\Launcher\GuideMe.SampleApp.Windows.Host.csproj
```

בחלון PowerShell שני, הפעלת המדריך:

```powershell
& "C:\GuideMe\GuideMe.exe" --guide sampleapp-windows-guide
```

## 3. מדריך Web

בחלון PowerShell ראשון, הפעלת אפליקציית הדוגמה:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\GuideMe.SampleApp.Web.Host.csproj
```

בחלון PowerShell שני, הפעלת המדריך:

```powershell
& "C:\GuideMe\GuideMe.exe" --guide sampleapp-web-guide
```

כאשר Web מופעלת ראשונה, היא מקימה את שרת הדוגמה. בהרצת Windows במקביל יש להוסיף `--use-existing-server` כדי למנוע התנגשות בפורט 5201.

## 4. מדריך משולב Web → Windows → Web

יש להפעיל **שתי** אפליקציות דוגמה, כל אחת בחלון PowerShell נפרד, ולהשאיר אותן פתוחות.

חלון ראשון — Web (מעלה גם את השרת בפורט 5201):

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\GuideMe.SampleApp.Web.Host.csproj
```

חלון שני — Windows, עם שימוש בשרת הקיים:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Windows\Launcher\GuideMe.SampleApp.Windows.Host.csproj -- --use-existing-server
```

חלון שלישי — הפעלת המדריך המשולב:

```powershell
& "C:\GuideMe\GuideMe.exe" --guide sampleapp-mixed-smoke-guide
```

מפתח המדריך:

```text
sampleapp-mixed-smoke-guide
```

## 5. הפעלת Instructor המקומפל

להפעלת ה־Instructor מתוך תיקיית הפרסום (ללא `dotnet run`):

```powershell
& "C:\GuideMe\InstructorWeb\GuideMe.Instructor.Web.exe"
```

## 6. הפעלה וכיבוי של אבחון Runtime

ברירת המחדל היא **אבחון כבוי**. להפעלת רישום אבחוני ב־Web Learner וב־Windows Learner, יש להגדיר את משתנה הסביבה `GUIDEME_DIAGNOSTICS` לערך `1` **לפני הפעלת GuideMe**.

הפעלה בחלון PowerShell הנוכחי:

```powershell
$env:GUIDEME_DIAGNOSTICS = "1"
& "C:\GuideMe\GuideMe.exe" --guide sampleapp-web-guide
```

למדריך Windows, יש להחליף את מפתח המדריך ב־`sampleapp-windows-guide`. כאשר המדריך מופעל מתוך ה־Instructor, יש להגדיר את המשתנה **לפני הפעלת ה־Instructor** כדי שהתהליך המופעל יירש אותו.

כיבוי בחלון PowerShell הנוכחי:

```powershell
Remove-Item Env:\GUIDEME_DIAGNOSTICS -ErrorAction SilentlyContinue
```

לאחר הכיבוי, יש להפעיל מחדש את GuideMe / ה־Instructor כדי שההרצות החדשות יתחילו ללא אבחון. הגדרה זו חלה על חלון PowerShell הנוכחי ועל תהליכים שמופעלים ממנו; היא אינה מגדירה משתנה סביבה קבוע במערכת.

- `GUIDEME_DIAGNOSTICS=1` — מפעיל לוגים אבחוניים ומדידות ביצועים מותנות.
- משתנה חסר או ערך אחר — האבחון המותנה כבוי.
- שגיאות מהותיות ממשיכות להירשם ללא תלות במצב האבחון.
- השם הישן `GUIDEME_VARIABLE_DIAGNOSTICS` אינו נתמך.

## 7. מצב אימות נוכחי

- מדריך Web (`sampleapp-web-guide`): **55/55 שלבים — תקין**.
- מדריך Windows (`sampleapp-windows-guide`): **55/55 שלבים — תקין**.
- מדריך משולב (`sampleapp-mixed-smoke-guide`): **6/6 שלבים — תקין**, כולל מעבר Web → Windows → Web.
- סך הכול **116/116 שלבים** אומתו בהפעלה ישירה של ה-Learner המותקן מול אפליקציות הדוגמה.
- במדריך Web, רכיב `testcrm-header-home::primary` משתמש בנתיב המסגרת `iframe[name='guideMe-header']`. מזהי `testcrm-*` משמשים כמפתחות פנימיים, לרבות הפניות `capture`, ואין לשנותם באופן גורף.

## 8. כללי עבודה

- אין להפעיל את המדריכים באמצעות `--learner-web`, `--learner-windows` או מזהי מדריכים מספריים.
- הפקודות לעיל מפעילות את גרסת הייצור מתוך `C:\GuideMe`, לא עותק ישן מתיקיית `bin`.
- לאחר שינוי בקוד יש לבצע Build ולפרסם מחדש ל-`C:\GuideMe`, כולל `NativeHost\GuideMe.NativeHost.exe` באמצעות סקריפט הפרסום.
- מסד ברירת המחדל הוא `C:\ProgramData\GuideMe\Data\GuideMe-V5.db`; זהו מסד הנתונים הפעיל היחיד.
- אין להריץ רגרסיה דרך תיקיות בדיקות E2E; יש לבדוק את ה-Learner מול אפליקציות הדוגמה כמוצר.
- עבודה ב-Git מתבצעת בענף `main` בלבד, ללא ענפים נפרדים.
- אין ליצור גיבויים או עותקי עבודה של מסדי נתונים באופן יזום; רק לפי בקשה מפורשת.
- מסמכי MD מתארים מצב פעיל בלבד, ללא יומן שינויים.
