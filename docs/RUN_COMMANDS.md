# פקודות הרצה מאומתות — DAP / SampleApp

> עודכן: 15.10.2026. פקודות הרצת שלושת המדריכים אומתו בפועל במסגרת הרגרסיה לאחר הסרת AutomationValue.
>
> **כלל:** הפרמטר `--guide` מקבל את **Key** של המדריך, לא את השדה המספרי `Guides.Id`. אין להשתמש ב-`--guide 965`.

## 1. הכנה: עדכון ופרסום גרסת DAP

יש לסגור את DAP לפני פרסום לתיקיית הייצור. הפקודות מתוך PowerShell:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull --ff-only origin main
dotnet publish .\src\DAP.App\DAP.App.csproj -c Release -r win-x64 --self-contained false -o C:\GuideMe
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

אין להוסיף `--use-existing-server` להרצת אפליקציית הדוגמה; ההפעלה התקינה שאומתה היא ללא הפרמטר.

## 4. מדריך משולב Web → Windows → Web

יש להפעיל **שתי** אפליקציות דוגמה, כל אחת בחלון PowerShell נפרד, ולהשאיר אותן פתוחות.

חלון ראשון — Windows:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj
```

חלון שני — Web:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
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
- לפני רגרסיה של תיקוני קוד חדשים יש לפרסם מחדש ל-`C:\GuideMe`.
- אין להריץ רגרסיה דרך תיקיות בדיקות E2E; יש לבדוק את ה-Learner מול אפליקציות הדוגמה כמוצר.
- עבודה ב-Git מתבצעת בענף `main` בלבד, ללא ענפים נפרדים.
