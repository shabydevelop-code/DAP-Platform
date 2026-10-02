using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

const string appTitle = "DAP Test CRM - Windows";
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var appProject = Path.Combine(root, "test-apps", "DAP.TestCRM", "Windows", "DAP.TestCRM.Windows.csproj");

using var app = Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{appProject}\" --no-launch-profile")
{
    WorkingDirectory = root,
    UseShellExecute = false
}) ?? throw new Exception("Could not start Windows TestCRM.");

try
{
    var window = Wait(() => AutomationElement.RootElement.FindFirst(TreeScope.Children,
        new PropertyCondition(AutomationElement.NameProperty, appTitle)), "main window", 30000);

    AutomationElement ById(string id) => Wait(() => window.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, id)), id);

    void Click(AutomationElement e, bool twice=false)
    {
        if(!e.TryGetCurrentPattern(ScrollItemPattern.Pattern,out var sp)){}
        else ((ScrollItemPattern)sp).ScrollIntoView();
        var r=e.Current.BoundingRectangle;
        if(r.IsEmpty) throw new Exception($"Target '{e.Current.AutomationId}' has no bounds.");
        var x=(int)(r.Left+r.Width/2); var y=(int)(r.Top+r.Height/2);
        SetCursorPos(x,y); mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero);
        if(twice){Thread.Sleep(80);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
        Thread.Sleep(800);
    }
    void Set(string id,string value)
    {
        var e=ById(id);
        if(!e.TryGetCurrentPattern(ValuePattern.Pattern,out var p)) throw new Exception($"{id} has no ValuePattern.");
        ((ValuePattern)p).SetValue(value); Thread.Sleep(120);
    }
    void Select(string id,string value)
    {
        var c=ById(id); Click(c);
        var item=Wait(()=>window.FindFirst(TreeScope.Descendants,new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty,value))),$"{id} item '{value}'");
        Click(item); Thread.Sleep(850);
    }
    void FirstGridRow(string gridId)
    {
        var g=ById(gridId);
        var row=Wait(()=>g.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)),gridId+" first row");
        Click(row,true); Thread.Sleep(600);
    }
    void ConfirmDialog()
    {
        var dlg=Wait(()=>window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Window)),"dialog");
        var buttons=dlg.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
        if(buttons.Count==0) throw new Exception("Dialog has no buttons.");
        var yes=buttons.Cast<AutomationElement>().FirstOrDefault(x=>x.Current.Name is "Yes" or "כן" or "אישור") ?? buttons[0];
        Click(yes); Thread.Sleep(500);
    }
    void DismissDialog()
    {
        var dlg=Wait(()=>window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Window)),"dialog");
        var b=dlg.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button))
            ?? throw new Exception("Dialog has no button.");
        Click(b); Thread.Sleep(400);
    }

    // Same representative CRM business flow used by Web: Customer -> Site -> Case -> Lead.
    Set("CustomerNameSearch","אלפא פתרונות בע\"מ"); Click(ById("SearchCustomersButton")); FirstGridRow("CustomersGrid");
    FirstGridRow("SitesGrid"); Click(ById("CasesTab")); Click(ById("SortCasesByStatusButton"));
    Click(ById("NewCaseButton")); Set("CaseSubject","תקלה בחיבור לאינטרנט"); Set("CaseDescription","הלקוח מדווח על חיבור לא יציב.");
    Click(ById("SaveCaseButton"));
    Select("CaseStatus","בטיפול"); Set("CaseResolutionNotes","בוצעה בדיקת שירות מול הלקוח והתקלה טופלה.");
    Click(ById("ActivityMoreButton")); DismissDialog();
    Select("CaseStatus","סגורה");
    Set("CaseSubject","תקלה בחיבור לאינטרנט");
    Click(ById("SaveCaseButton")); DismissDialog();
    Select("CaseCloseReason","טופל"); Click(ById("SaveCaseButton"));

    Click(ByButtonName("מטה תל אביב")); // Site breadcrumb
    Click(ById("LeadsTab")); Click(ById("CasesTab")); Click(ById("LeadsTab"));
    Click(ById("NewLeadButton")); Set("LeadContactName","לקוח בדיקת מערכת"); Click(ById("SaveLeadButton"));
    Select("LeadStatus","נסגר בהצלחה"); Select("LeadStatus","חדש"); Select("LeadStatus","נסגר בהצלחה");
    Click(ById("SaveLeadButton")); DismissDialog();
    Select("LeadSelectedService","תמיכה מורחבת"); Click(ById("SaveLeadButton"));
    Click(ById("DeleteLeadButton")); ConfirmDialog();

    Click(ById("PortalHeader"));
    Set("CustomerNameSearch","אלפא פתרונות בע\"מ"); Click(ById("SearchCustomersButton")); FirstGridRow("CustomersGrid");
    FirstGridRow("SitesGrid"); Click(ById("LeadsTab")); FirstGridRow("LeadsGrid");
    Select("LeadStatus","חדש"); Select("LeadStatus","נסגר בהצלחה"); Select("LeadStatus","חדש"); Select("LeadStatus","נסגר בהצלחה");
    Click(ByButtonName("מטה תל אביב")); Click(ById("CasesTab")); FirstGridRow("CasesGrid"); Click(ByButtonName("מטה תל אביב"));
    var rows=ById("CasesGrid").FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem));
    if(rows.Count==0) throw new Exception("No Case available for final context check.");

    Click(ById("PortalHeader"));
    if(ById("CustomerNameSearch") is null) throw new Exception("Portal did not return to customer search.");

    Console.WriteLine("PASS: Windows CRM-only representative Customer -> Site -> Case -> Lead workflow completed.");
}
finally
{
    try { if(!app.HasExited) app.Kill(true); } catch {}
}

static AutomationElement Wait(Func<AutomationElement?> f,string what,int timeout=10000)
{
    var sw=Stopwatch.StartNew();
    while(sw.ElapsedMilliseconds<timeout){var x=f();if(x!=null)return x;Thread.Sleep(100);}
    throw new TimeoutException($"Timed out waiting for {what}.");
}

[DllImport("user32.dll")] static extern bool SetCursorPos(int X,int Y);
[DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
