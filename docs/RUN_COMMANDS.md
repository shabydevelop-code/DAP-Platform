# פקודות הרצה מאומתות — DAP / SampleApp

> עודכן: 16.10.2026. שלושת מסלולי המדריכים אומתו מתוך התקנת GuideMe V5 הפעילה.
>
> **כלל:** הפרמטר `--guide` מקבל את **Key** של המדריך, לא את השדה המספרי `Guides.Id`. אין להשתמש ב-`--guide 965`.

## 1. הכנה: עדכון ופרסום גרסת DAP

יש לסגור את DAP לפני פרסום לתיקיית הייצור. הפקודות מתוך PowerShell:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull --ff-only origin main
.\scripts\Publish-GuideMe.ps1
```

**סדר הרגרסיה המחייב:** עדכון → פרסום → הפעלת אפליקציות הדוגמה → הפעלת מדריך מתוך `C:\GuideMe` → רגרסיה.

## 2. מדריך Windows

בחלון PowerShell ראשון, הפעלת אפליקציית הדוגמה:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj
```

בחלון PowerShell שני, הפעלת המדריך:

```powershell
& "C:\GuideMe\DAP.exe" --guide sampleapp-windows-guide
```

## 3. מדריך Web

בחלון PowerShell ראשון, הפעלת אפליקציית הדוגמה:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
```

בחלון PowerShell שני, הפעלת המדריך:

```powershell
& "C:\GuideMe\DAP.exe" --guide sampleapp-web-guide
```

כאשר Web מופעלת ראשונה, היא מקימה את שרת הדוגמה. בהרצת Windows במקביל יש להוסיף `--use-existing-server` כדי למנוע התנגשות בפורט 5201.

## 4. מדריך משולב Web → Windows → Web

יש להפעיל **שתי** אפליקציות דוגמה, כל אחת בחלון PowerShell נפרד, ולהשאיר אותן פתוחות.

חלון ראשון — Web (מעלה גם את השרת בפורט 5201):

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
```

חלון שני — Windows, עם שימוש בשרת הקיים:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj -- --use-existing-server
```

חלון שלישי — הפעלת המדריך המשולב:

```powershell
& "C:\GuideMe\DAP.exe" --guide sampleapp-mixed-smoke-guide
```

מפתח המדריך אומת במסד הנתונים:

```text
Id:   965
Key:  sampleapp-mixed-smoke-guide
Name: SampleApp Mixed Web-Windows-Web Smoke
```

## 5. כללי עבודה

- אין להפעיל את המדריכים באמצעות `--learner-web`, `--learner-windows` או מזהי מדריכים מספריים.
- הפקודות לעיל מפעילות את גרסת הייצור מתוך `C:\GuideMe`, לא עותק ישן מתיקיית `bin`.
- לאחר שינוי בקוד יש לבצע Build ולפרסם מחדש ל-`C:\GuideMe`, כולל `NativeHost\GuideMe.NativeHost.exe` באמצעות סקריפט הפרסום.
- מסד ברירת המחדל הוא `C:\ProgramData\GuideMe\Data\GuideMe-V5.db`; אין להשתמש במסד הישן `GuideMe.db` להרצת V5.
- אין להריץ רגרסיה דרך תיקיות בדיקות E2E; יש לבדוק את ה-Learner מול אפליקציות הדוגמה כמוצר.
- עבודה ב-Git מתבצעת בענף `main` בלבד, ללא ענפים נפרדים.
