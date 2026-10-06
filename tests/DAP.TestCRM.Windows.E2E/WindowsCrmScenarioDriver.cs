using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using DAP.Core.Targets;

namespace DAP.TestCRM.Windows.E2E;

internal sealed class WindowsCrmScenarioDriver
{
    readonly AutomationElement window;
    readonly Process app;
    string? createdCaseId;
    string? activeStepId;
    int? activeStepOrder;
    bool visualMode;

    public WindowsCrmScenarioDriver(Process app, AutomationElement window, bool visualMode = false)
    {
        this.app=app;
        this.window=window;
        this.visualMode=visualMode;
    }

    public bool VisualMode => visualMode;
    public string? CreatedCaseId => createdCaseId;

    public void SetVisualMode(bool enabled) => visualMode = enabled;

    public void VisualPause(int milliseconds)
    {
        if (visualMode && milliseconds > 0)
            Thread.Sleep(milliseconds);
    }

    public void SetActiveGuideStep(int order, string id)
    {
        activeStepOrder = order;
        activeStepId = id;
    }

    AutomationElement ById(string id)=>Wait(()=>window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id)),id);
    AutomationElement ButtonByName(string name)=>Wait(()=>window.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,name))),$"button '{name}'");

    void Click(AutomationElement e,bool twice=false)
    {
        VisualTarget(e);
        if(!twice && e.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }

        // Some real applications expose actions (for example a WPF DataGrid
        // row opened by MouseDoubleClick) that cannot be completed through an
        // available UI Automation invoke pattern. Keep the same physical action
        // in Fast and Visual modes. VisualTarget above adds animated cursor
        // travel only in Visual mode; Fast jumps directly to the target here.
        if(e.TryGetCurrentPattern(ScrollItemPattern.Pattern,out var sp))
            ((ScrollItemPattern)sp).ScrollIntoView();

        var r=e.Current.BoundingRectangle;
        var wr=window.Current.BoundingRectangle;
        if(r.IsEmpty)throw new Exception($"Target '{e.Current.AutomationId}' has no bounds.");
        if(r.Left<wr.Left || r.Top<wr.Top || r.Right>wr.Right || r.Bottom>wr.Bottom)
            throw new Exception($"Refusing physical mouse fallback outside CRM window for '{e.Current.AutomationId}'.");

        SetCursorPos((int)(r.Left+r.Width/2),(int)(r.Top+r.Height/2));
        Mouse();
        if(twice){Thread.Sleep(80);Mouse();}
    }
    void Set(string id,string value)
    {
        var e=Wait(()=> {
            var candidate=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
            return candidate is not null && candidate.Current.IsEnabled ? candidate : null;
        },$"{id} enabled");
        if(!e.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern))
            throw new Exception($"{id} has no ValuePattern.");

        VisualTarget(e);
        e.SetFocus();
        ((ValuePattern)pattern).SetValue(value);

        // Match the Web runner's learner action: finish text entry with a real
        // focus traversal so the application and Runtime receive the natural blur.
        KeyPress(VK_TAB);
    }
    void Select(string id,string value)
    {
        var combo=ById(id);
        VisualTarget(combo);
        if(!combo.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern))
            throw new Exception($"{id} has no ValuePattern.");
        ((ValuePattern)pattern).SetValue(value);
    }

    public Task ApplyAutomationValue(TargetDescriptor target, string value)
    {
        if (target.Runtime != TargetRuntime.Windows)
            throw new InvalidOperationException("Windows Hybrid automation can act only on Windows targets.");
        if (!target.Locator.Strategy.Equals("automation-id", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Windows Hybrid automation supports persisted automation-id value targets; Step target uses '{target.Locator.Strategy}'.");

        var element = ById(target.Locator.Value);
        if (element.Current.ControlType == ControlType.ComboBox)
            Select(target.Locator.Value, value);
        else
            Set(target.Locator.Value, value);

        return Task.CompletedTask;
    }

    AutomationElement? EnabledById(string id)
    {
        var e=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
        return e is not null && e.Current.IsEnabled ? e : null;
    }
    void FirstRow(string id)
    {
        var g=ById(id);
        var row=Wait(()=>g.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)),id+" first row");

        // WPF DataGrid opens records from MouseDoubleClick. Keep the physical
        // double-click, but synchronize with selection first so the handler
        // receives the intended row even when the two clicks are very fast.
        if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))
            ((SelectionItemPattern)selection).Select();

        Click(row,true);
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
        var buttonList=buttons.Cast<AutomationElement>().ToList();
        LogModal(
            popupElement,
            buttonList,
            confirm ? "EXPECTED/CONFIRMATION" : "EXPECTED/VALIDATION");

        var button=confirm
            ? buttonList.FirstOrDefault(x=>x.Current.Name is "Yes" or "כן" or "אישור") ?? buttonList.FirstOrDefault()
            : buttonList.FirstOrDefault(x=>x.Current.Name is "OK" or "אישור") ?? buttonList.FirstOrDefault();
        if(button is null || !button.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
            throw new Exception("Modal dialog has no invokable button.");
        VisualTarget(button);
        ((InvokePattern)invoke).Invoke();
        WaitHandle(()=>!IsWindowVisible(popup) ? mainHwnd : IntPtr.Zero,"modal dialog dismissed");
    }

    void LogModal(
        AutomationElement popupElement,
        IReadOnlyList<AutomationElement> buttons,
        string classification)
    {
        string title;
        try { title = popupElement.Current.Name?.Trim() ?? string.Empty; }
        catch (ElementNotAvailableException) { title = "<unavailable>"; }

        var text = popupElement.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(x =>
            {
                try { return x.Current.ControlType == ControlType.Text; }
                catch (ElementNotAvailableException) { return false; }
            })
            .Select(x =>
            {
                try { return x.Current.Name?.Trim(); }
                catch (ElementNotAvailableException) { return null; }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();

        var buttonNames = buttons
            .Select(x =>
            {
                try { return x.Current.Name?.Trim(); }
                catch (ElementNotAvailableException) { return null; }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var step = activeStepOrder is null
            ? "<unknown>"
            : $"{activeStepOrder}:{activeStepId}";

        Console.WriteLine(
            $"[WINDOWS MODAL] step={step}; class={classification}; " +
            $"title='{title}'; text='{string.Join(" | ", text)}'; " +
            $"buttons=[{string.Join(", ", buttonNames)}]");
    }

    public Task SetCustomerSearch(string v){Set("CustomerNameSearch",v);return Task.CompletedTask;}

    public Task SetCustomerSearchWithoutCommit(string value)
    {
        var element=Wait(()=> {
            var candidate=window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty,"CustomerNameSearch"));
            return candidate is not null && candidate.Current.IsEnabled ? candidate : null;
        },"CustomerNameSearch enabled");
        if(!element.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern))
            throw new Exception("CustomerNameSearch has no ValuePattern.");

        element.SetFocus();
        ((ValuePattern)pattern).SetValue(value);
        return Task.CompletedTask;
    }

    public Task CommitCustomerSearchEdit()
    {
        // Commit the edit the same way a learner does: keep focus in the
        // CustomerNameSearch editor and send a real TAB keystroke so WPF
        // performs its natural focus traversal / blur lifecycle.
        var element=ById("CustomerNameSearch");
        element.SetFocus();
        KeyPress(VK_TAB);
        return Task.CompletedTask;
    }
    public Task SubmitCustomerSearch()
    {
        Click(ById("SearchCustomersButton"));
        return Task.CompletedTask;
    }
    public Task OpenFirstCustomer()
    {
        FirstRow("CustomersGrid");
        return Task.CompletedTask;
    }

    public Task OpenSiteByName(string name)
    {
        var row=RowByCellText("SitesGrid",name);
        if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))
            ((SelectionItemPattern)selection).Select();

        Click(row,true);
        return Task.CompletedTask;
    }

    public Task OpenFirstSite() => OpenSiteByName("מטה תל אביב");
    public Task OpenCases()
    {
        Click(ById("CasesTab"));
        return Task.CompletedTask;
    }
    public Task SortCasesByStatus()
    {
        var oldButton=ById("NewCaseButton");
        Click(ById("SortCasesByStatusButton"));

        // ShowSite rebuilds the whole Cases screen asynchronously. Seeing a
        // NewCaseButton is not enough: the old button remains in UIA until the
        // replacement screen is installed. Wait for a different UIA element.
        Wait(()=>
        {
            var current=EnabledById("NewCaseButton");
            return current is not null && !Automation.Compare(oldButton,current) ? current : null;
        },"Cases screen replacement after status sort");
        return Task.CompletedTask;
    }
    public Task CreateCase()
    {
        var button=ById("NewCaseButton");
        VisualTarget(button);
        if(!button.Current.IsEnabled || !button.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
            throw new Exception("NewCaseButton is not invokable.");
        ((InvokePattern)invoke).Invoke();
        return Task.CompletedTask;
    }
    public Task SetCaseSubject(string v){Set("CaseSubject",v);return Task.CompletedTask;}
    public Task SetCaseDescription(string v){Set("CaseDescription",v);return Task.CompletedTask;}
    public Task SaveCase()
    {
        Click(ById("SaveCaseButton"));
        return Task.CompletedTask;
    }

    public void CaptureCreatedCaseId()
    {
        createdCaseId = CurrentCaseId();
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
            foreach(var row in grid.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)).Cast<AutomationElement>())
            {
                foreach(var cell in row.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>())
                {
                    if(string.Equals(cell.Current.Name?.Trim(),value,StringComparison.Ordinal))
                        return row;
                    if(cell.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern) &&
                       string.Equals(((ValuePattern)pattern).Current.Value?.Trim(),value,StringComparison.Ordinal))
                        return row;
                }
            }
            return null;
        },$"{gridId} row containing '{value}'");
    }

    public Task OpenCreatedCase()
    {
        if(string.IsNullOrWhiteSpace(createdCaseId))throw new Exception("Created Case id is not known.");

        var row=RowByCellText("CasesGrid",createdCaseId);
        if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))
            ((SelectionItemPattern)selection).Select();

        Click(row,true);
        return Task.CompletedTask;
    }

    public Task SetCaseStatus(string v){Select("CaseStatus",v);return Task.CompletedTask;}
    public Task SetResolutionNotes(string v){Set("CaseResolutionNotes",v);return Task.CompletedTask;}
    public Task ShowMoreActivity(){Click(ById("ActivityMoreButton"));return Task.CompletedTask;}
    public Task DismissValidation(){DialogButton(false);return Task.CompletedTask;}
    public Task SetCloseReason(string v){Select("CaseCloseReason",v);return Task.CompletedTask;}
    public Task OpenSiteFromBreadcrumb()
    {

        var siteCrumb=window.FindAll(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty,"Breadcrumb")))
            .Cast<AutomationElement>()
            .FirstOrDefault(x=>x.Current.Name=="מטה תל אביב")
            ?? throw new Exception("Site breadcrumb 'מטה תל אביב' was not found.");

        // Invoke exactly once. Re-invoking an async WPF breadcrumb while ShowSite is
        // still loading starts overlapping ShowSite operations and can replace the
        // newly rendered Site screen with another in-flight render.
        VisualTarget(siteCrumb);
        if(!siteCrumb.Current.IsEnabled || !siteCrumb.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
            throw new Exception("Site breadcrumb is not invokable.");
        ((InvokePattern)invoke).Invoke();
        return Task.CompletedTask;
    }
    public Task OpenLeads()
    {
        Click(ById("LeadsTab"));
        return Task.CompletedTask;
    }
    public Task OpenCustomerFromBreadcrumb()
    {
        var customer=window.FindAll(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty,"Breadcrumb")))
            .Cast<AutomationElement>()
            .FirstOrDefault(x=>x.Current.Name=="אלפא פתרונות בע\"מ")
            ?? throw new Exception("Customer breadcrumb was not found.");

        VisualTarget(customer);
        if(!customer.Current.IsEnabled || !customer.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
            throw new Exception("Customer breadcrumb is not invokable.");
        ((InvokePattern)invoke).Invoke();
        return Task.CompletedTask;
    }
    public Task OpenFirstLead()
    {
        FirstRow("LeadsGrid");
        return Task.CompletedTask;
    }
    public Task OpenLeadByContactName(string contactName)
    {
        var row=RowByCellText("LeadsGrid",contactName);
        if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))
            ((SelectionItemPattern)selection).Select();

        Click(row,true);
        return Task.CompletedTask;
    }

    public Task OpenFirstCase()
    {
        FirstRow("CasesGrid");
        return Task.CompletedTask;
    }
    public Task OpenCaseBySubject(string subject)
    {
        var row=RowByCellText("CasesGrid",subject);
        if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))
            ((SelectionItemPattern)selection).Select();

        Click(row,true);
        return Task.CompletedTask;
    }
    public Task CreateLead()
    {
        var button=ById("NewLeadButton");
        VisualTarget(button);
        if(!button.Current.IsEnabled || !button.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))
            throw new Exception("NewLeadButton is not invokable.");
        ((InvokePattern)invoke).Invoke();
        return Task.CompletedTask;
    }
    public Task SetLeadContact(string v){Set("LeadContactName",v);return Task.CompletedTask;}
    public Task SaveLead()
    {
        Click(ById("SaveLeadButton"));
        return Task.CompletedTask;
    }
    public Task SetLeadStatus(string v){Select("LeadStatus",v);return Task.CompletedTask;}
    public Task SetLeadService(string v){Select("LeadSelectedService",v);return Task.CompletedTask;}
    public Task DeleteLead(){Click(ById("DeleteLeadButton"));return Task.CompletedTask;}
    public Task ConfirmDelete()
    {
        DialogButton(true);
        return Task.CompletedTask;
    }
    public Task DeleteCase(){Click(ById("DeleteCaseButton"));return Task.CompletedTask;}
    public Task GoPortal(){Click(ById("PortalHeader"));return Task.CompletedTask;}

    public void VisualTarget(AutomationElement element)
    {
        if (!visualMode)
            return;

        // Visual mode must preserve the exact same application action path as
        // fast mode. Only add cursor movement and presentation delay here.
        // Scrolling is owned by the learner/runtime, not by the E2E visual driver.
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty)
            return;

        var targetX = (int)(bounds.Left + bounds.Width / 2);
        var targetY = (int)(bounds.Top + bounds.Height / 2);

        if (!GetCursorPos(out var point))
        {
            SetCursorPos(targetX, targetY);
            Thread.Sleep(220);
            return;
        }

        const int frames = 12;
        for (var frame = 1; frame <= frames; frame++)
        {
            var progress = frame / (double)frames;
            var eased = 1 - Math.Pow(1 - progress, 3);
            var x = (int)Math.Round(point.X + (targetX - point.X) * eased);
            var y = (int)Math.Round(point.Y + (targetY - point.Y) * eased);
            SetCursorPos(x, y);
            Thread.Sleep(18);
        }

        Thread.Sleep(120);
    }

    IntPtr WaitHandle(Func<IntPtr> f,string what,int timeout=5000)
    {
        var sw=Stopwatch.StartNew();
        while(sw.ElapsedMilliseconds<timeout)
        {
            if(app.HasExited)
                throw new TargetApplicationClosedException();

            var x=f();
            if(x!=IntPtr.Zero)return x;
            Thread.Sleep(100);
        }

        if(app.HasExited)
            throw new TargetApplicationClosedException();

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    AutomationElement Wait(Func<AutomationElement?> f,string what,int timeout=5000)
    {
        var sw=Stopwatch.StartNew();
        while(sw.ElapsedMilliseconds<timeout)
        {
            if(app.HasExited)
                throw new TargetApplicationClosedException();

            try
            {
                var x=f();
                if(x!=null)return x;
            }
            catch(ElementNotAvailableException) when(app.HasExited)
            {
                throw new TargetApplicationClosedException();
            }

            Thread.Sleep(100);
        }

        if(app.HasExited)
            throw new TargetApplicationClosedException();

        throw new TimeoutException($"Timed out waiting for {what}.");
    }
    const uint GW_ENABLEDPOPUP=6, WM_COMMAND=0x0111;
    const int IDOK=1, IDYES=6;
    const int SM_XVIRTUALSCREEN=76, SM_YVIRTUALSCREEN=77, SM_CXVIRTUALSCREEN=78, SM_CYVIRTUALSCREEN=79;
    const byte VK_RETURN=0x0D;
    const byte VK_TAB=0x09;
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
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte virtualKey,byte scanCode,uint flags,UIntPtr extra);
}

internal sealed class TargetApplicationClosedException : Exception
{
}
