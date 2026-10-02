using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using DAP.TestCRM.E2E.Common;

namespace DAP.TestCRM.Windows.E2E;

internal sealed class WindowsCrmScenarioDriver : ICrmScenarioDriver
{
    readonly AutomationElement window;
    readonly Process app;

    public WindowsCrmScenarioDriver(Process app, AutomationElement window){this.app=app;this.window=window;}

    AutomationElement ById(string id)=>Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id)),id);
    AutomationElement ButtonByName(string name)=>Wait(()=>window.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,name))),$"button '{name}'");

    void Click(AutomationElement e,bool twice=false)
    {
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
        var combo=ById(id);
        if(combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var ep))
            ((ExpandCollapsePattern)ep).Expand();
        else Click(combo);

        var processId=window.Current.ProcessId;
        var item=Wait(()=>AutomationElement.RootElement.FindFirst(TreeScope.Descendants,new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty,processId),
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty,value))),$"{id} item '{value}'");

        if(item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var sip))
            ((SelectionItemPattern)sip).Select();
        else if(item.TryGetCurrentPattern(InvokePattern.Pattern,out var iip))
            ((InvokePattern)iip).Invoke();
        else
            throw new Exception($"List item '{value}' exposes neither SelectionItemPattern nor InvokePattern.");

        Wait(()=> {
            var current=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
            return current is not null && current.Current.IsEnabled ? current : null;
        },$"{id} refreshed");
        Thread.Sleep(250);
    }
    void FirstRow(string id){var g=ById(id);var row=Wait(()=>g.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)),id+" first row");Click(row,true);}

    AutomationElement Dialog()=>Wait(()=>AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Window)).Cast<AutomationElement>().FirstOrDefault(x=>x.Current.ProcessId==app.Id&&x.Current.Name!="DAP Test CRM - Windows"),"dialog");
    void DialogButton(bool confirm){var d=Dialog();var bs=d.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));if(bs.Count==0)throw new Exception("Dialog has no buttons.");var b=confirm?bs.Cast<AutomationElement>().FirstOrDefault(x=>x.Current.Name is "Yes" or "כן" or "אישור")??bs[0]:bs[0];Click(b);}

    public Task SearchCustomer(string v){Set("CustomerNameSearch",v);Click(ById("SearchCustomersButton"));return Task.CompletedTask;}
    public Task OpenFirstCustomer(){FirstRow("CustomersGrid");return Task.CompletedTask;}
    public Task OpenFirstSite(){FirstRow("SitesGrid");return Task.CompletedTask;}
    public Task OpenCases(){Click(ById("CasesTab"));return Task.CompletedTask;}
    public Task SortCasesByStatus(){Click(ById("SortCasesByStatusButton"));return Task.CompletedTask;}
    public Task CreateCase(){Click(ById("NewCaseButton"));return Task.CompletedTask;}
    public Task SetCaseSubject(string v){Set("CaseSubject",v);return Task.CompletedTask;}
    public Task SetCaseDescription(string v){Set("CaseDescription",v);return Task.CompletedTask;}
    public Task SaveCase(){Click(ById("SaveCaseButton"));return Task.CompletedTask;}
    public Task SetCaseStatus(string v){Select("CaseStatus",v);return Task.CompletedTask;}
    public Task SetResolutionNotes(string v){Set("CaseResolutionNotes",v);return Task.CompletedTask;}
    public Task ShowMoreActivity(){Click(ById("ActivityMoreButton"));return Task.CompletedTask;}
    public Task DismissValidation(){DialogButton(false);return Task.CompletedTask;}
    public Task SetCloseReason(string v){Select("CaseCloseReason",v);return Task.CompletedTask;}
    public Task OpenSiteFromBreadcrumb(){Click(ButtonByName("מטה תל אביב"));return Task.CompletedTask;}
    public Task OpenLeads(){Click(ById("LeadsTab"));return Task.CompletedTask;}
    public Task CreateLead(){Click(ById("NewLeadButton"));return Task.CompletedTask;}
    public Task SetLeadContact(string v){Set("LeadContactName",v);return Task.CompletedTask;}
    public Task SaveLead(){Click(ById("SaveLeadButton"));return Task.CompletedTask;}
    public Task SetLeadStatus(string v){Select("LeadStatus",v);return Task.CompletedTask;}
    public Task SetLeadService(string v){Select("LeadSelectedService",v);return Task.CompletedTask;}
    public Task DeleteLead(){Click(ById("DeleteLeadButton"));return Task.CompletedTask;}
    public Task ConfirmDelete(){DialogButton(true);return Task.CompletedTask;}
    public Task GoPortal(){Click(ById("PortalHeader"));return Task.CompletedTask;}

    static AutomationElement Wait(Func<AutomationElement?> f,string what,int timeout=10000){var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<timeout){var x=f();if(x!=null)return x;Thread.Sleep(100);}throw new TimeoutException($"Timed out waiting for {what}.");}
    static void Mouse(){mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
