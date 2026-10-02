using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using DAP.TestCRM.E2E.Common;

namespace DAP.TestCRM.Windows.E2E;

internal sealed class WindowsCrmScenarioDriver : ICrmScenarioDriver
{
    readonly AutomationElement window;
    readonly Process app;
    string? createdCaseId;

    public WindowsCrmScenarioDriver(Process app, AutomationElement window){this.app=app;this.window=window;}

    AutomationElement ById(string id)=>Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id)),id);
    AutomationElement ButtonByName(string name)=>Wait(()=>window.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,name))),$"button '{name}'");

    void Click(AutomationElement e,bool twice=false)
    {
        DismissUnexpectedInfoDialogs();
        if(!twice && e.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            Thread.Sleep(500);
            return;
        }

        if(e.TryGetCurrentPattern(ScrollItemPattern.Pattern,out var sp))((ScrollItemPattern)sp).ScrollIntoView();
        var r=e.Current.BoundingRectangle;
        var wr=window.Current.BoundingRectangle;
        if(r.IsEmpty)throw new Exception($"Target '{e.Current.AutomationId}' has no bounds.");
        if(r.Left<wr.Left || r.Top<wr.Top || r.Right>wr.Right || r.Bottom>wr.Bottom)
            throw new Exception($"Refusing physical mouse fallback outside CRM window for '{e.Current.AutomationId}'.");

        SetCursorPos((int)(r.Left+r.Width/2),(int)(r.Top+r.Height/2));
        Mouse();
        if(twice){Thread.Sleep(80);Mouse();}
        Thread.Sleep(800);
    }
    void Set(string id,string value)
    {
        DismissUnexpectedInfoDialogs();
        var e=Wait(()=> {
            var candidate=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
            return candidate is not null && candidate.Current.IsEnabled ? candidate : null;
        },$"{id} enabled");
        if(!e.TryGetCurrentPattern(ValuePattern.Pattern,out var p))throw new Exception($"{id} has no ValuePattern.");
        ((ValuePattern)p).SetValue(value);
        Thread.Sleep(150);
    }
    void Select(string id,string value)
    {
        DismissUnexpectedInfoDialogs();
        var combo=ById(id);
        if(!combo.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern))
            throw new Exception($"{id} has no ValuePattern.");
        ((ValuePattern)pattern).SetValue(value);
        Wait(()=> {
            var current=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
            if(current is null || !current.TryGetCurrentPattern(ValuePattern.Pattern,out var currentPattern))return null;
            return string.Equals(((ValuePattern)currentPattern).Current.Value,value,StringComparison.Ordinal) ? current : null;
        },$"{id} value '{value}'");

        if(id=="CaseStatus" && value=="בטיפול")
            Wait(()=>EnabledById("CaseResolutionNotes"),"CaseResolutionNotes enabled after CaseStatus=בטיפול");
        else if(id=="CaseStatus" && value=="סגורה")
            Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"CaseCloseReason")),"CaseCloseReason after CaseStatus=סגורה");
        else if(id=="LeadStatus" && value=="נסגר בהצלחה")
            Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"LeadSelectedService")),"LeadSelectedService after LeadStatus=נסגר בהצלחה");
        else if(id=="LeadStatus" && value=="חדש")
            Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"LeadSelectedService")) is null ? window : null,"LeadSelectedService hidden after LeadStatus=חדש");
        else
            Thread.Sleep(400);
    }

    AutomationElement? EnabledById(string id)
    {
        var e=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
        return e is not null && e.Current.IsEnabled ? e : null;
    }
    void FirstRow(string id){var g=ById(id);var row=Wait(()=>g.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)),id+" first row");Click(row,true);}

    void DismissUnexpectedInfoDialogs()
    {
        var mainHwnd=new IntPtr(window.Current.NativeWindowHandle);
        for(var i=0;i<4;i++)
        {
            var popup=GetWindow(mainHwnd,GW_ENABLEDPOPUP);
            if(popup==IntPtr.Zero || popup==mainHwnd || !IsWindowVisible(popup))return;

            var popupElement=AutomationElement.FromHandle(popup);
            var buttons=popupElement.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button))
                .Cast<AutomationElement>().ToList();

            var ok=buttons.FirstOrDefault(x=>x.Current.Name is "OK" or "אישור");
            var yes=buttons.FirstOrDefault(x=>x.Current.Name is "Yes" or "כן");
            if(ok is null || yes is not null)return;
            if(!ok.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))return;

            ((InvokePattern)invoke).Invoke();
            WaitHandle(()=>!IsWindowVisible(popup) ? mainHwnd : IntPtr.Zero,"unexpected information dialog dismissed");
        }
    }

    void DialogButton(bool confirm)
    {
        var mainHwnd=new IntPtr(window.Current.NativeWindowHandle);
        var popup=WaitHandle(()=>
        {
            var hwnd=GetWindow(mainHwnd,GW_ENABLEDPOPUP);
            return hwnd!=IntPtr.Zero && hwnd!=mainHwnd ? hwnd : IntPtr.Zero;
        },"modal dialog");
        var popupElement=AutomationElement.FromHandle(popup);
        var buttons=popupElement.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
        var button=confirm
            ? buttons.Cast<AutomationElement>().FirstOrDefault(x=>x.Current.Name is "Yes" or "כן" or "אישור") ?? buttons.Cast<AutomationElement>().FirstOrDefault()
            : buttons.Cast<AutomationElement>().FirstOrDefault(x=>x.Current.Name is "OK" or "אישור") ?? buttons.Cast<AutomationElement>().FirstOrDefault();
        if(button is null || !button.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
            throw new Exception("Modal dialog has no invokable button.");
        ((InvokePattern)invoke).Invoke();
        WaitHandle(()=>!IsWindowVisible(popup) ? mainHwnd : IntPtr.Zero,"modal dialog dismissed");
    }

    public Task SetCustomerSearch(string v){Set("CustomerNameSearch",v);return Task.CompletedTask;}
    public Task SubmitCustomerSearch()
    {
        Click(ById("SearchCustomersButton"));
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"CustomersGrid")),"customer search results");
        return Task.CompletedTask;
    }
    public Task OpenFirstCustomer(){FirstRow("CustomersGrid");return Task.CompletedTask;}
    public Task OpenFirstSite(){FirstRow("SitesGrid");return Task.CompletedTask;}
    public Task OpenCases()
    {
        DismissUnexpectedInfoDialogs();
        Click(ById("CasesTab"));
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"NewCaseButton")),"Cases screen");
        return Task.CompletedTask;
    }
    public Task SortCasesByStatus(){Click(ById("SortCasesByStatusButton"));return Task.CompletedTask;}
    public Task CreateCase()
    {
        var deadline=Stopwatch.StartNew();
        while(deadline.ElapsedMilliseconds<10000)
        {
            var subject=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"CaseSubject"));
            if(subject is not null && subject.Current.IsEnabled)return Task.CompletedTask;

            var button=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"NewCaseButton"));
            if(button is not null && button.Current.IsEnabled && button.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
                ((InvokePattern)invoke).Invoke();

            Thread.Sleep(300);
        }
        throw new TimeoutException("NewCaseButton was invoked but the Case form did not become ready.");
    }
    public Task SetCaseSubject(string v){Set("CaseSubject",v);return Task.CompletedTask;}
    public Task SetCaseDescription(string v){Set("CaseDescription",v);return Task.CompletedTask;}
    public Task SaveCase()
    {
        var wasPersisted=window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteCaseButton")) is not null;
        Click(ById("SaveCaseButton"));

        if(!wasPersisted)
            Wait(()=>window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteCaseButton")),"persisted Case form after Save");
        else
            WaitForOperationCompleted("Case Save");

        createdCaseId ??= CurrentCaseId();
        return Task.CompletedTask;
    }

    void WaitForOperationCompleted(string operation)
    {
        var status=ById("StatusText");
        var sw=Stopwatch.StartNew();
        var observedBusy=false;
        while(sw.ElapsedMilliseconds<5000)
        {
            var text=status.Current.Name;
            if(text=="מעבד...")observedBusy=true;
            if(observedBusy && string.IsNullOrEmpty(text))return;
            Thread.Sleep(50);
        }

        // A very fast operation can complete before UIA observes the busy text.
        // In that case require the current screen to be stable after the click.
        Thread.Sleep(250);
        if(string.IsNullOrEmpty(status.Current.Name))return;
        throw new TimeoutException($"Timed out waiting for {operation} to complete.");
    }
    string CurrentCaseId()
    {
        var titles=window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text))
            .Cast<AutomationElement>()
            .Select(x=>x.Current.Name)
            .Where(x=>x.StartsWith("פניה ",StringComparison.Ordinal));
        foreach(var title in titles)
        {
            var id=title["פניה ".Length..].Trim();
            if(int.TryParse(id,out _))return id;
        }
        throw new Exception("Persisted Case id was not found in the Windows Case screen.");
    }

    AutomationElement RowByCellText(string gridId,string value)
    {
        var grid=ById(gridId);
        return Wait(()=>
        {
            foreach(var row in grid.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)).Cast<AutomationElement>())
            {
                if(row.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text))
                    .Cast<AutomationElement>().Any(x=>string.Equals(x.Current.Name,value,StringComparison.Ordinal)))
                    return row;
            }
            return null;
        },$"{gridId} row containing '{value}'");
    }

    public Task OpenCreatedCase()
    {
        if(string.IsNullOrWhiteSpace(createdCaseId))throw new Exception("Created Case id is not known.");
        Click(RowByCellText("CasesGrid",createdCaseId),true);
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteCaseButton")),"created Case form");
        if(CurrentCaseId()!=createdCaseId)throw new Exception($"Expected created Case {createdCaseId}, but another Case was opened.");
        return Task.CompletedTask;
    }

    public Task SetCaseStatus(string v){Select("CaseStatus",v);return Task.CompletedTask;}
    public Task SetResolutionNotes(string v){Set("CaseResolutionNotes",v);return Task.CompletedTask;}
    public Task ShowMoreActivity(){Click(ById("ActivityMoreButton"));return Task.CompletedTask;}
    public Task DismissValidation(){DialogButton(false);return Task.CompletedTask;}
    public Task SetCloseReason(string v){Select("CaseCloseReason",v);return Task.CompletedTask;}
    public Task OpenSiteFromBreadcrumb()
    {
        DismissUnexpectedInfoDialogs();
        var deadline=Stopwatch.StartNew();
        while(deadline.ElapsedMilliseconds<5000)
        {
            var leadsTab=window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty,"LeadsTab"));
            if(leadsTab is not null)return Task.CompletedTask;

            var siteCrumb=window.FindAll(TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                    new PropertyCondition(AutomationElement.AutomationIdProperty,"Breadcrumb")))
                .Cast<AutomationElement>()
                .FirstOrDefault(x=>x.Current.Name=="מטה תל אביב");

            if(siteCrumb is not null &&
               siteCrumb.Current.IsEnabled &&
               siteCrumb.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
                ((InvokePattern)invoke).Invoke();

            Thread.Sleep(200);
        }
        throw new TimeoutException("Site breadcrumb was invoked but the Site screen did not become ready.");
    }
    public Task OpenLeads()
    {
        DismissUnexpectedInfoDialogs();
        Click(ById("LeadsTab"));
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"NewLeadButton")),"Leads screen");
        return Task.CompletedTask;
    }
    public Task OpenCustomerFromBreadcrumb()
    {
        DismissUnexpectedInfoDialogs();
        var customer=window.FindAll(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty,"Breadcrumb")))
            .Cast<AutomationElement>()
            .FirstOrDefault(x=>x.Current.Name=="אלפא פתרונות בע\"מ")
            ?? throw new Exception("Customer breadcrumb was not found.");
        Click(customer);
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"SitesGrid")),"Customer Sites screen");
        return Task.CompletedTask;
    }
    public Task OpenFirstLead()
    {
        FirstRow("LeadsGrid");
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteLeadButton")),"Lead form");
        return Task.CompletedTask;
    }
    public Task OpenFirstCase()
    {
        FirstRow("CasesGrid");
        Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteCaseButton")),"Case form");
        return Task.CompletedTask;
    }
    public Task CreateLead()
    {
        var deadline=Stopwatch.StartNew();
        while(deadline.ElapsedMilliseconds<5000)
        {
            DismissUnexpectedInfoDialogs();
            var contact=EnabledById("LeadContactName");
            if(contact is not null)return Task.CompletedTask;

            var button=window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty,"NewLeadButton"));
            if(button is not null && button.Current.IsEnabled &&
               button.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
                ((InvokePattern)invoke).Invoke();

            Thread.Sleep(200);
        }
        throw new TimeoutException("NewLeadButton was invoked but the Lead form did not become ready.");
    }
    public Task SetLeadContact(string v){Set("LeadContactName",v);return Task.CompletedTask;}
    public Task SaveLead()
    {
        Click(ById("SaveLeadButton"));
        var deleteButton=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteLeadButton"));
        if(deleteButton is null)
            Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DeleteLeadButton")),"persisted Lead form after Save");
        return Task.CompletedTask;
    }
    public Task SetLeadStatus(string v){Select("LeadStatus",v);return Task.CompletedTask;}
    public Task SetLeadService(string v){Select("LeadSelectedService",v);return Task.CompletedTask;}
    public Task DeleteLead(){Click(ById("DeleteLeadButton"));return Task.CompletedTask;}
    public Task ConfirmDelete(){DialogButton(true);return Task.CompletedTask;}
    public Task DeleteCase(){Click(ById("DeleteCaseButton"));return Task.CompletedTask;}
    public Task GoPortal(){Click(ById("PortalHeader"));return Task.CompletedTask;}

    static IntPtr WaitHandle(Func<IntPtr> f,string what,int timeout=5000){var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<timeout){var x=f();if(x!=IntPtr.Zero)return x;Thread.Sleep(100);}throw new TimeoutException($"Timed out waiting for {what}.");}
    static AutomationElement Wait(Func<AutomationElement?> f,string what,int timeout=5000){var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<timeout){var x=f();if(x!=null)return x;Thread.Sleep(100);}throw new TimeoutException($"Timed out waiting for {what}.");}
    const uint GW_ENABLEDPOPUP=6, WM_COMMAND=0x0111;
    const int IDOK=1, IDYES=6;
    const int SM_XVIRTUALSCREEN=76, SM_YVIRTUALSCREEN=77, SM_CXVIRTUALSCREEN=78, SM_CYVIRTUALSCREEN=79;
    const byte VK_RETURN=0x0D;
    const byte VK_HOME=0x24;
    const byte VK_DOWN=0x28;
    const uint KEYEVENTF_KEYUP=0x0002;
    static void KeyPress(byte key){keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,KEYEVENTF_KEYUP,UIntPtr.Zero);}
    static void Mouse(){mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd,uint uCmd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte virtualKey,byte scanCode,uint flags,UIntPtr extra);
}
