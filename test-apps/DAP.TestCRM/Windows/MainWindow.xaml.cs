using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;

namespace DAP.TestCRM.Windows;

public partial class MainWindow : Window
{
    readonly CrmApiClient api=new();
    int customerId,siteId;
    string siteTab="details";
    string caseSort="id",caseSortDir="desc";
    public MainWindow(){InitializeComponent();Loaded+=async(_,_)=>await ShowSearch();}

    async void PortalButton_Click(object s,RoutedEventArgs e)=>await ShowSearch();
    Button B(string text,string id,RoutedEventHandler click){var b=new Button{Content=text,Margin=new(4),Padding=new(12,6,12,6)};AutomationProperties.SetAutomationId(b,id);AutomationProperties.SetName(b,text);b.Click+=click;return b;}
    void H(Panel p, string title, Button button)
    {
        var content=new StackPanel{Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft};
        content.Children.Add(new TextBlock{Text=title,FontSize=20,VerticalAlignment=VerticalAlignment.Center});
        content.Children.Add(button);
        var row=new DockPanel{LastChildFill=false,FlowDirection=FlowDirection.LeftToRight};
        DockPanel.SetDock(content,Dock.Right);
        row.Children.Add(content);
        p.Children.Add(row);
    }
    void A(Panel p, params Button[] buttons)
    {
        var actions=new StackPanel{Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft};
        foreach(var button in buttons) actions.Children.Add(button);
        var row=new DockPanel{LastChildFill=false,FlowDirection=FlowDirection.LeftToRight};
        DockPanel.SetDock(actions,Dock.Right);
        row.Children.Add(actions);
        p.Children.Add(row);
    }
    TextBox T(string id,string text=""){var x=new TextBox{Text=text,Margin=new(0,4,0,0),Width=360,HorizontalAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetAutomationId(x,id);return x;}
    ComboBox C(string id,string[] items,string value){var x=new AutomationComboBox{Margin=new(0,4,0,0),Width=360,HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=items,SelectedItem=value};AutomationProperties.SetAutomationId(x,id);return x;}
    void F(Panel p,string label,Control control)
    {
        var row=new Grid{FlowDirection=FlowDirection.LeftToRight,HorizontalAlignment=HorizontalAlignment.Stretch};
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(360)});
        var field=new Grid{Width=360,Margin=new(0,5,0,5),FlowDirection=FlowDirection.LeftToRight};
        field.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        field.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var caption=new TextBlock{Text=label,Width=360,Margin=new(0,0,0,2),FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Right,FlowDirection=FlowDirection.LeftToRight,HorizontalAlignment=HorizontalAlignment.Stretch};
        var accessibleLabel=label.TrimEnd(' ','*');
        AutomationProperties.SetName(control,accessibleLabel);
        AutomationProperties.SetLabeledBy(control,caption);
        Grid.SetRow(caption,0);
        control.FlowDirection=FlowDirection.RightToLeft;
        control.HorizontalAlignment=HorizontalAlignment.Stretch;
        control.Width=double.NaN;
        Grid.SetRow(control,1);
        field.Children.Add(caption);
        field.Children.Add(control);
        Grid.SetColumn(field,1);
        row.Children.Add(field);
        p.Children.Add(row);
    }
    StackPanel V()=>new(){Orientation=Orientation.Vertical,HorizontalAlignment=HorizontalAlignment.Stretch,FlowDirection=FlowDirection.RightToLeft,Margin=new(24,0,24,0)};
    ScrollViewer S(UIElement content)=>new(){Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,FlowDirection=FlowDirection.RightToLeft,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    void Crumbs(params (string text,Func<Task>? go)[] xs){BreadcrumbPanel.Children.Clear();foreach(var x in xs){if(x.go==null)BreadcrumbPanel.Children.Add(new TextBlock{Text=x.text+" / ",Margin=new(2)});else{var b=B(x.text,"Breadcrumb",async(_,_)=>await x.go());b.Padding=new(2);b.Margin=new(2);BreadcrumbPanel.Children.Add(b);}}}
    async Task Safe(Func<Task> f){try{StatusText.Text="מעבד...";await f();}catch(Exception ex){MessageBox.Show(this,ex.Message,"DAP Test CRM",MessageBoxButton.OK,MessageBoxImage.Error);}finally{StatusText.Text="";}}

    async Task ShowSearch(string name="",string phone="",string email="",bool run=false)
    {
        Crumbs(("פורטל לקוחות",null));var root=V();root.Children.Add(new TextBlock{Text="חיפוש לקוח",FontSize=24,FontWeight=FontWeights.SemiBold});
        var n=T("CustomerNameSearch",name);var p=T("CustomerPhoneSearch",phone);var m=T("CustomerEmailSearch",email);
        F(root,"שם לקוח",n);F(root,"טלפון",p);F(root,"דוא\"ל",m);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft};actions.Children.Add(B("חיפוש","SearchCustomersButton",async(_,_)=>await ShowSearch(n.Text,p.Text,m.Text,true)));actions.Children.Add(B("לקוח חדש","NewCustomerButton",async(_,_)=>await ShowCustomerForm()));var actionRow=new DockPanel{LastChildFill=false,FlowDirection=FlowDirection.LeftToRight};DockPanel.SetDock(actions,Dock.Right);actionRow.Children.Add(actions);root.Children.Add(actionRow);
        if(run){var data=await api.GetCustomersAsync(name,phone,email);var g=GridFor(data,new[]{"Id","Name","Phone","Email"},"CustomersGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is Customer x)await ShowCustomer(x.Id);};root.Children.Add(new TextBlock{Text=data.Count==0?"לא נמצאו לקוחות התואמים לחיפוש.":"תוצאות חיפוש",FontSize=18,Margin=new(4,14,4,4)});root.Children.Add(g);}
        ScreenHost.Content=S(root);
    }
    async Task ShowCustomerForm(){Crumbs(("פורטל לקוחות",()=>ShowSearch()),("לקוח חדש",null));var v=V();v.Children.Add(new TextBlock{Text="לקוח חדש",FontSize=24});var n=T("CustomerName"); var p=T("CustomerPhone"); var e=T("CustomerEmail");foreach(var z in new[]{("שם *",n),("טלפון *",p),("דוא\"ל *",e)})F(v,z.Item1,z.Item2);A(v,B("שמור","SaveCustomerButton",async(_,_)=>await Safe(async()=>{var x=await api.CreateCustomerAsync(n.Text,p.Text,e.Text);await ShowCustomer(x.Id);})));ScreenHost.Content=v;await Task.CompletedTask;}
    async Task ShowCustomer(int id){customerId=id;var c=await api.GetCustomerAsync(id);if(c==null)return;var sites=await api.GetSitesAsync(id);Crumbs(("פורטל לקוחות",()=>ShowSearch()),(c.Name,null));var v=V();v.Children.Add(new TextBlock{Text="לקוח",FontSize=14,FontWeight=FontWeights.SemiBold});v.Children.Add(new TextBlock{Text=c.Name,FontSize=24});v.Children.Add(new TextBlock{Text=$"{c.Phone} · {c.Email}",Margin=new(4)});H(v,"אתרים",B("אתר חדש","NewSiteButton",async(_,_)=>await ShowSiteForm(id)));var g=GridFor(sites,new[]{"Id","Name","Type","Address"},"SitesGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is Site x)await ShowSite(x.Id,"details");};v.Children.Add(g);ScreenHost.Content=v;}
    async Task ShowSiteForm(int cid){var customer=await api.GetCustomerAsync(cid);if(customer==null)return;Crumbs(("פורטל לקוחות",()=>ShowSearch()),(customer.Name,()=>ShowCustomer(cid)),("אתר חדש",null));var v=V();var n=T("SiteName"); var t=C("SiteType",["","משרד","סניף","מחסן"],""); var a=T("SiteAddress");v.Children.Add(new TextBlock{Text="אתר חדש",FontSize=24});F(v,"שם *",n);F(v,"סוג *",t);F(v,"כתובת *",a);A(v,B("שמור","SaveSiteButton",async(_,_)=>await Safe(async()=>{var x=await api.CreateSiteAsync(cid,new(n.Text,t.SelectedItem?.ToString()??"",a.Text));await ShowSite(x.Id,"details");})));ScreenHost.Content=v;await Task.CompletedTask;}

    async Task ShowSite(int id,string tab)
    {
        siteId=id;siteTab=tab;var s=await api.GetSiteAsync(id);if(s==null)return;customerId=s.CustomerId;var customer=await api.GetCustomerAsync(s.CustomerId);if(customer==null)return;Crumbs(("פורטל לקוחות",()=>ShowSearch()),(customer.Name,()=>ShowCustomer(s.CustomerId)),(s.Name,null));var v=V();v.Children.Add(new TextBlock{Text="אתר",FontSize=14,FontWeight=FontWeights.SemiBold});v.Children.Add(new TextBlock{Text=s.Name,FontSize=24});
        var nav=new StackPanel{Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft};nav.Children.Add(B("פרטי אתר","SiteDetailsTab",async(_,_)=>await ShowSite(id,"details")));nav.Children.Add(B("פניות","CasesTab",async(_,_)=>await ShowSite(id,"cases")));nav.Children.Add(B("לידים","LeadsTab",async(_,_)=>await ShowSite(id,"leads")));var navRow=new DockPanel{LastChildFill=false,FlowDirection=FlowDirection.LeftToRight};DockPanel.SetDock(nav,Dock.Right);navRow.Children.Add(nav);v.Children.Add(navRow);
        if(tab=="details"){var n=T("SiteName",s.Name); var t=C("SiteType",["","משרד","סניף","מחסן"],s.Type); var a=T("SiteAddress",s.Address);F(v,"שם *",n);F(v,"סוג *",t);F(v,"כתובת *",a);A(v,B("שמור שינויים","SaveSiteButton",async(_,_)=>await Safe(async()=>{await api.SaveSiteAsync(id,new(n.Text,t.SelectedItem?.ToString()??"",a.Text));await ShowSite(id,"details");})),B("מחק אתר","DeleteSiteButton",async(_,_)=>await Safe(async()=>{if(MessageBox.Show("למחוק את האתר?","אישור מחיקה",MessageBoxButton.YesNo)==MessageBoxResult.Yes){await api.DeleteSiteAsync(id);await ShowCustomer(s.CustomerId);}})));}
        if(tab=="cases")
        {
            var newCase=B("פניה חדשה","NewCaseButton",async(_,_)=>await ShowCase(null,id));
            var sortCases=B("מיין לפי סטטוס","SortCasesByStatusButton",async(_,_)=>
            {
                caseSort="status";
                caseSortDir=caseSortDir=="asc"?"desc":"asc";
                await ShowSite(id,"cases");
            });
            H(v,"פניות",newCase);
            A(v,sortCases);
            var xs=await api.GetCasesAsync(id,caseSort,caseSortDir);
            var g=GridFor(xs,new[]{"Id","Status","Subject"},"CasesGrid");
            g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is CaseItem x)await ShowCase(x.Id,id);};
            v.Children.Add(g);
        }
        if(tab=="leads"){H(v,"לידים",B("ליד חדש","NewLeadButton",async(_,_)=>await ShowLead(null,id)));var xs=await api.GetLeadsAsync(id);var g=GridFor(xs,new[]{"Id","Source","ContactName","Status"},"LeadsGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is Lead x)await ShowLead(x.Id,id);};v.Children.Add(g);}
        ScreenHost.Content=S(v);
    }

    async Task ShowCase(int? id,int sid)
    {
        var fresh=id==null;var x=fresh?new CaseItem(0,sid,"פתוחה","","",""):await api.GetCaseAsync(id!.Value);if(x==null)return;var s=await api.GetSiteAsync(sid);if(s==null)return;var customer=await api.GetCustomerAsync(s.CustomerId);if(customer==null)return;Crumbs(("פורטל לקוחות",()=>ShowSearch()),(customer.Name,()=>ShowCustomer(s.CustomerId)),(s.Name,()=>ShowSite(sid,"cases")),(fresh?"פניה חדשה":"פניה "+id,null));var v=V();v.Children.Add(new TextBlock{Text="פנייה",FontSize=14,FontWeight=FontWeights.SemiBold});v.Children.Add(new TextBlock{Text=fresh?"פניה חדשה":"פניה "+id,FontSize=24});var st=C("CaseStatus",["","פתוחה","בטיפול","סגורה"],x.Status); var sub=T("CaseSubject",x.Subject); var desc=T("CaseDescription",x.Description); var close=C("CaseCloseReason",["","טופל","בקשת הלקוח","כפילות","לא רלוונטי"],x.CloseReason);F(v,"סטטוס *",st);F(v,"נושא *",sub);F(v,"תיאור",desc);var resolution=T("CaseResolutionNotes");resolution.IsEnabled=x.Status!="פתוחה";F(v,"הערות טיפול",resolution);if(x.Status=="סגורה")F(v,"סיבת סגירה *",close);
        var activity=new StackPanel{Margin=new(0,18,0,0)};
        activity.Children.Add(new TextBlock{Text="היסטוריית פעילות",FontSize=18,FontWeight=FontWeights.SemiBold});
        for(var i=1;i<=12;i++)activity.Children.Add(new TextBlock{Text=$"פעילות {i} — עדכון שירות מתועד בפנייה",Margin=new(0,3,0,3)});
        var moreActivity=B("הצג פעילות נוספת","ActivityMoreButton",(_,_)=>{});var moreActivityRow=new DockPanel{LastChildFill=false,FlowDirection=FlowDirection.LeftToRight};DockPanel.SetDock(moreActivity,Dock.Right);moreActivityRow.Children.Add(moreActivity);activity.Children.Add(moreActivityRow);
        v.Children.Add(activity);
        st.SelectionChanged+=async(_,_)=>{if(!fresh&&st.SelectedItem is string q)await Safe(async()=>{var change=await api.CaseStatusChangedAsync(id!.Value,q);sub.Text=change.Subject;close.SelectedItem=change.CloseReason;resolution.IsEnabled=change.ResolutionEnabled;if(change.Status=="סגורה"){if(close.Parent is null)F(v,"סיבת סגירה *",close);}else if(close.Parent is Panel closeParent)closeParent.Children.Remove(close);});};
        var saveCase=B("שמור","SaveCaseButton",async(_,_)=>await Safe(async()=>{var input=new CaseInput(st.SelectedItem?.ToString()??"",sub.Text,desc.Text,close.SelectedItem?.ToString()??"");if(fresh){var y=await api.CreateCaseAsync(sid,input);await ShowCase(y.Id,sid);}else{await api.SaveCaseAsync(id!.Value,input);await ShowCase(id,sid);}}));
        if(!fresh) A(v,saveCase,B("מחק פנייה","DeleteCaseButton",async(_,_)=>await Safe(async()=>{if(MessageBox.Show("למחוק את הפנייה?","אישור מחיקה",MessageBoxButton.YesNo)==MessageBoxResult.Yes){await api.DeleteCaseAsync(id!.Value);await ShowSite(sid,"cases");}})));
        else A(v,saveCase);
        ScreenHost.Content=S(v);
    }
    async Task ShowLead(int? id,int sid)
    {
        var fresh=id==null;var x=fresh?new Lead(0,sid,"אתר אינטרנט","","חדש","","",""):await api.GetLeadAsync(id!.Value);if(x==null)return;var s=await api.GetSiteAsync(sid);if(s==null)return;var customer=await api.GetCustomerAsync(s.CustomerId);if(customer==null)return;Crumbs(("פורטל לקוחות",()=>ShowSearch()),(customer.Name,()=>ShowCustomer(s.CustomerId)),(s.Name,()=>ShowSite(sid,"leads")),(fresh?"ליד חדש":"ליד "+id,null));var v=V();v.Children.Add(new TextBlock{Text="ליד",FontSize=14,FontWeight=FontWeights.SemiBold});v.Children.Add(new TextBlock{Text=fresh?"ליד חדש":"ליד "+id,FontSize=24});var src=C("LeadSource",["","אתר אינטרנט","הפניה","קמפיין","טלפון"],x.Source); var contact=T("LeadContactName",x.ContactName); var st=C("LeadStatus",["","חדש","בתהליך","נסגר בהצלחה","נסגר ללא עסקה"],x.Status); var notes=T("LeadNotes",x.Notes); var service=C("LeadSelectedService",["","חבילת שירות","שדרוג מערכת","הדרכה","תמיכה מורחבת"],x.SelectedService); var lost=C("LeadLostReason",["","מחיר","נדחה למועד אחר","בחר ספק אחר","לא רלוונטי"],x.LostReason);F(v,"מקור *",src);F(v,"איש קשר *",contact);F(v,"סטטוס *",st);F(v,"הערות",notes);if(x.Status=="נסגר בהצלחה")F(v,"שירות שנבחר *",service);if(x.Status=="נסגר ללא עסקה")F(v,"סיבת אי-סגירה *",lost);
        st.SelectionChanged+=async(_,_)=>{if(!fresh&&st.SelectedItem is string q)await Safe(async()=>{var change=await api.LeadStatusChangedAsync(id!.Value,q);service.SelectedItem=change.SelectedService;lost.SelectedItem=change.LostReason;if(change.Status=="נסגר בהצלחה"){if(service.Parent is null)F(v,"שירות שנבחר *",service);}else if(service.Parent is Panel serviceParent)serviceParent.Children.Remove(service);if(change.Status=="נסגר ללא עסקה"){if(lost.Parent is null)F(v,"סיבת אי-סגירה *",lost);}else if(lost.Parent is Panel lostParent)lostParent.Children.Remove(lost);});};
        var saveLead=B("שמור","SaveLeadButton",async(_,_)=>await Safe(async()=>{var input=new LeadInput(src.SelectedItem?.ToString()??"",contact.Text,st.SelectedItem?.ToString()??"",notes.Text,service.SelectedItem?.ToString()??"",lost.SelectedItem?.ToString()??"");if(fresh){var y=await api.CreateLeadAsync(sid,input);await ShowLead(y.Id,sid);}else{await api.SaveLeadAsync(id!.Value,input);await ShowLead(id,sid);}}));
        if(!fresh) A(v,saveLead,B("מחק ליד","DeleteLeadButton",async(_,_)=>await Safe(async()=>{if(MessageBox.Show("למחוק את הליד?","אישור מחיקה",MessageBoxButton.YesNo)==MessageBoxResult.Yes){await api.DeleteLeadAsync(id!.Value);await ShowSite(sid,"leads");}})));
        else A(v,saveLead);
        ScreenHost.Content=S(v);
    }
    static DataGrid GridFor<T>(IEnumerable<T> items,string[] fields,string id){var g=new DataGrid{ItemsSource=items,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,Margin=new(4),MinHeight=180,Height=double.NaN,MaxHeight=double.PositiveInfinity,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,FlowDirection=FlowDirection.RightToLeft,HorizontalContentAlignment=HorizontalAlignment.Right};AutomationProperties.SetAutomationId(g,id);AutomationProperties.SetName(g,id switch{"CustomersGrid"=>"תוצאות חיפוש לקוחות","SitesGrid"=>"רשימת אתרים","CasesGrid"=>"רשימת פניות","LeadsGrid"=>"רשימת לידים",_=>"טבלה"});foreach(var f in fields)g.Columns.Add(new DataGridTextColumn{Header=f,Binding=new Binding(f),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});g.LoadingRow+=(_,e)=>{var itemId=e.Row.Item switch{Customer x=>x.Id,Site x=>x.Id,CaseItem x=>x.Id,Lead x=>x.Id,_=>0};if(itemId>0){AutomationProperties.SetAutomationId(e.Row,$"{id}Row_{itemId}");AutomationProperties.SetName(e.Row,$"{id} row {itemId}");}};return g;}
}
