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
    public MainWindow(){InitializeComponent();Loaded+=async(_,_)=>await ShowSearch();}

    async void PortalButton_Click(object s,RoutedEventArgs e)=>await ShowSearch();
    Button B(string text,string id,RoutedEventHandler click){var b=new Button{Content=text,Margin=new(4),Padding=new(12,6,12,6)};AutomationProperties.SetAutomationId(b,id);b.Click+=click;return b;}
    TextBox T(string id,string text=""){var x=new TextBox{Text=text,Margin=new(0,4,0,0),Width=360,HorizontalAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetAutomationId(x,id);return x;}
    ComboBox C(string id,string[] items,string value){var x=new ComboBox{Margin=new(0,4,0,0),Width=360,HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=items,SelectedItem=value};AutomationProperties.SetAutomationId(x,id);return x;}
    void F(Panel p,string label,Control control){var field=new StackPanel{Width=360,HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft,Margin=new(0,5,0,5)};field.Children.Add(new TextBlock{Text=label,Margin=new(0,0,0,2),FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Right});control.HorizontalAlignment=HorizontalAlignment.Stretch;control.Width=double.NaN;field.Children.Add(control);p.Children.Add(field);}
    StackPanel V()=>new(){Orientation=Orientation.Vertical,HorizontalAlignment=HorizontalAlignment.Stretch,FlowDirection=FlowDirection.RightToLeft,Margin=new(24,0,24,0)};
    ScrollViewer S(UIElement content)=>new(){Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,FlowDirection=FlowDirection.RightToLeft,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    void Crumbs(params (string text,Func<Task>? go)[] xs){BreadcrumbPanel.Children.Clear();foreach(var x in xs){if(x.go==null)BreadcrumbPanel.Children.Add(new TextBlock{Text=x.text+" / ",Margin=new(2)});else{var b=B(x.text,"Breadcrumb",async(_,_)=>await x.go());b.Padding=new(2);b.Margin=new(2);BreadcrumbPanel.Children.Add(b);}}}
    async Task Safe(Func<Task> f){try{StatusText.Text="מעבד...";await f();StatusText.Text="";}catch(Exception ex){StatusText.Text="שגיאה";MessageBox.Show(this,ex.Message,"DAP Test CRM",MessageBoxButton.OK,MessageBoxImage.Error);}}

    async Task ShowSearch(string name="",string phone="",string email="",bool run=false)
    {
        Crumbs(("פורטל לקוחות",null));var root=V();root.Children.Add(new TextBlock{Text="חיפוש לקוח",FontSize=24,FontWeight=FontWeights.SemiBold});
        var n=T("CustomerNameSearch",name);var p=T("CustomerPhoneSearch",phone);var m=T("CustomerEmailSearch",email);
        F(root,"שם לקוח",n);F(root,"טלפון",p);F(root,"דוא\"ל",m);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft};actions.Children.Add(B("חיפוש","SearchCustomersButton",async(_,_)=>await ShowSearch(n.Text,p.Text,m.Text,true)));actions.Children.Add(B("לקוח חדש","NewCustomerButton",async(_,_)=>await ShowCustomerForm()));root.Children.Add(actions);
        if(run){var data=await api.GetCustomersAsync(name,phone,email);var g=GridFor(data,new[]{"Id","Name","Phone","Email"},"CustomersGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is Customer x)await ShowCustomer(x.Id);};root.Children.Add(new TextBlock{Text=data.Count==0?"לא נמצאו לקוחות התואמים לחיפוש.":"תוצאות חיפוש",FontSize=18,Margin=new(4,14,4,4)});root.Children.Add(g);}
        ScreenHost.Content=S(root);
    }
    async Task ShowCustomerForm(){Crumbs(("פורטל לקוחות",()=>ShowSearch()),("לקוח חדש",null));var v=V();v.Children.Add(new TextBlock{Text="לקוח חדש",FontSize=24});var n=T("CustomerName"); var p=T("CustomerPhone"); var e=T("CustomerEmail");foreach(var z in new[]{("שם *",n),("טלפון *",p),("דוא\"ל *",e)})F(v,z.Item1,z.Item2);v.Children.Add(B("שמור","SaveCustomerButton",async(_,_)=>await Safe(async()=>{var x=await api.CreateCustomerAsync(n.Text,p.Text,e.Text);await ShowCustomer(x.Id);})));ScreenHost.Content=v;await Task.CompletedTask;}
    async Task ShowCustomer(int id){customerId=id;var c=await api.GetCustomerAsync(id);if(c==null)return;var sites=await api.GetSitesAsync(id);Crumbs(("פורטל לקוחות",()=>ShowSearch()),(c.Name,null));var v=V();v.Children.Add(new TextBlock{Text=c.Name,FontSize=24});v.Children.Add(new TextBlock{Text=$"{c.Phone} · {c.Email}",Margin=new(4)});var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft};bar.Children.Add(new TextBlock{Text="אתרים",FontSize=20,VerticalAlignment=VerticalAlignment.Center});bar.Children.Add(B("אתר חדש","NewSiteButton",async(_,_)=>await ShowSiteForm(id)));v.Children.Add(bar);var g=GridFor(sites,new[]{"Id","Name","Type","Address"},"SitesGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is Site x)await ShowSite(x.Id,"details");};v.Children.Add(g);ScreenHost.Content=v;}
    async Task ShowSiteForm(int cid){Crumbs(("פורטל לקוחות",()=>ShowSearch()),("לקוח",()=>ShowCustomer(cid)),("אתר חדש",null));var v=V();var n=T("SiteName"); var t=C("SiteType",["","משרד","סניף","מחסן"],""); var a=T("SiteAddress");v.Children.Add(new TextBlock{Text="אתר חדש",FontSize=24});F(v,"שם *",n);F(v,"סוג *",t);F(v,"כתובת *",a);v.Children.Add(B("שמור","SaveSiteButton",async(_,_)=>await Safe(async()=>{var x=await api.CreateSiteAsync(cid,new(n.Text,t.SelectedItem?.ToString()??"",a.Text));await ShowSite(x.Id,"details");})));ScreenHost.Content=v;await Task.CompletedTask;}

    async Task ShowSite(int id,string tab)
    {
        siteId=id;siteTab=tab;var s=await api.GetSiteAsync(id);if(s==null)return;customerId=s.CustomerId;Crumbs(("פורטל לקוחות",()=>ShowSearch()),("לקוח",()=>ShowCustomer(s.CustomerId)),(s.Name,null));var v=V();v.Children.Add(new TextBlock{Text=s.Name,FontSize=24});
        var nav=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft};nav.Children.Add(B("פרטי אתר","SiteDetailsTab",async(_,_)=>await ShowSite(id,"details")));nav.Children.Add(B("פניות","CasesTab",async(_,_)=>await ShowSite(id,"cases")));nav.Children.Add(B("לידים","LeadsTab",async(_,_)=>await ShowSite(id,"leads")));v.Children.Add(nav);
        if(tab=="details"){var n=T("SiteName",s.Name); var t=C("SiteType",["","משרד","סניף","מחסן"],s.Type); var a=T("SiteAddress",s.Address);F(v,"שם *",n);F(v,"סוג *",t);F(v,"כתובת *",a);v.Children.Add(B("שמור שינויים","SaveSiteButton",async(_,_)=>await Safe(async()=>{await api.SaveSiteAsync(id,new(n.Text,t.SelectedItem?.ToString()??"",a.Text));await ShowSite(id,"details");})));v.Children.Add(B("מחק אתר","DeleteSiteButton",async(_,_)=>await Safe(async()=>{if(MessageBox.Show("למחוק את האתר?","אישור מחיקה",MessageBoxButton.YesNo)==MessageBoxResult.Yes){await api.DeleteSiteAsync(id);await ShowCustomer(s.CustomerId);}})));}
        if(tab=="cases"){var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft};bar.Children.Add(new TextBlock{Text="פניות",FontSize=20});bar.Children.Add(B("פניה חדשה","NewCaseButton",async(_,_)=>await ShowCase(null,id)));v.Children.Add(bar);var xs=await api.GetCasesAsync(id);var g=GridFor(xs,new[]{"Id","Status","Subject"},"CasesGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is CaseItem x)await ShowCase(x.Id,id);};v.Children.Add(g);}
        if(tab=="leads"){var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft};bar.Children.Add(new TextBlock{Text="לידים",FontSize=20});bar.Children.Add(B("ליד חדש","NewLeadButton",async(_,_)=>await ShowLead(null,id)));v.Children.Add(bar);var xs=await api.GetLeadsAsync(id);var g=GridFor(xs,new[]{"Id","Source","ContactName","Status"},"LeadsGrid");g.MouseDoubleClick+=async(_,_)=>{if(g.SelectedItem is Lead x)await ShowLead(x.Id,id);};v.Children.Add(g);}
        ScreenHost.Content=S(v);
    }

    async Task ShowCase(int? id,int sid)
    {
        var fresh=id==null;var x=fresh?new CaseItem(0,sid,"פתוחה","","",""):await api.GetCaseAsync(id!.Value);if(x==null)return;var s=await api.GetSiteAsync(sid);if(s==null)return;Crumbs(("פורטל לקוחות",()=>ShowSearch()),("לקוח",()=>ShowCustomer(s.CustomerId)),(s.Name,()=>ShowSite(sid,"cases")),(fresh?"פניה חדשה":"פניה "+id,null));var v=V();v.Children.Add(new TextBlock{Text=fresh?"פניה חדשה":"פניה "+id,FontSize=24});var st=C("CaseStatus",["","פתוחה","בטיפול","סגורה"],x.Status); var sub=T("CaseSubject",x.Subject); var desc=T("CaseDescription",x.Description); var close=C("CaseCloseReason",["","טופל","בקשת הלקוח","כפילות","לא רלוונטי"],x.CloseReason);F(v,"סטטוס *",st);F(v,"נושא *",sub);F(v,"תיאור",desc);var resolution=T("CaseResolutionNotes");resolution.IsEnabled=x.Status!="פתוחה";F(v,"הערות טיפול",resolution);if(x.Status=="סגורה")F(v,"סיבת סגירה *",close);
        st.SelectionChanged+=async(_,_)=>{if(!fresh&&st.SelectedItem is string q)await Safe(async()=>{await api.CaseStatusChangedAsync(id!.Value,q);await ShowCase(id,sid);});};
        v.Children.Add(B("שמור","SaveCaseButton",async(_,_)=>await Safe(async()=>{var input=new CaseInput(st.SelectedItem?.ToString()??"",sub.Text,desc.Text,close.SelectedItem?.ToString()??"");if(fresh){var y=await api.CreateCaseAsync(sid,input);await ShowCase(y.Id,sid);}else{await api.SaveCaseAsync(id!.Value,input);await ShowCase(id,sid);}})));
        if(!fresh)v.Children.Add(B("מחק פנייה","DeleteCaseButton",async(_,_)=>await Safe(async()=>{if(MessageBox.Show("למחוק את הפנייה?","אישור מחיקה",MessageBoxButton.YesNo)==MessageBoxResult.Yes){await api.DeleteCaseAsync(id!.Value);await ShowSite(sid,"cases");}})));
        ScreenHost.Content=S(v);
    }
    async Task ShowLead(int? id,int sid)
    {
        var fresh=id==null;var x=fresh?new Lead(0,sid,"אתר אינטרנט","","חדש","","",""):await api.GetLeadAsync(id!.Value);if(x==null)return;var s=await api.GetSiteAsync(sid);if(s==null)return;Crumbs(("פורטל לקוחות",()=>ShowSearch()),("לקוח",()=>ShowCustomer(s.CustomerId)),(s.Name,()=>ShowSite(sid,"leads")),(fresh?"ליד חדש":"ליד "+id,null));var v=V();v.Children.Add(new TextBlock{Text=fresh?"ליד חדש":"ליד "+id,FontSize=24});var src=C("LeadSource",["","אתר אינטרנט","הפניה","קמפיין","טלפון"],x.Source); var contact=T("LeadContactName",x.ContactName); var st=C("LeadStatus",["","חדש","בתהליך","נסגר בהצלחה","נסגר ללא עסקה"],x.Status); var notes=T("LeadNotes",x.Notes); var service=C("LeadSelectedService",["","חבילת שירות","שדרוג מערכת","הדרכה","תמיכה מורחבת"],x.SelectedService); var lost=C("LeadLostReason",["","מחיר","נדחה למועד אחר","בחר ספק אחר","לא רלוונטי"],x.LostReason);F(v,"מקור *",src);F(v,"איש קשר *",contact);F(v,"סטטוס *",st);F(v,"הערות",notes);if(x.Status=="נסגר בהצלחה")F(v,"שירות שנבחר *",service);if(x.Status=="נסגר ללא עסקה")F(v,"סיבת אי-סגירה *",lost);
        st.SelectionChanged+=async(_,_)=>{if(!fresh&&st.SelectedItem is string q)await Safe(async()=>{await api.LeadStatusChangedAsync(id!.Value,q);await ShowLead(id,sid);});};
        v.Children.Add(B("שמור","SaveLeadButton",async(_,_)=>await Safe(async()=>{var input=new LeadInput(src.SelectedItem?.ToString()??"",contact.Text,st.SelectedItem?.ToString()??"",notes.Text,service.SelectedItem?.ToString()??"",lost.SelectedItem?.ToString()??"");if(fresh){var y=await api.CreateLeadAsync(sid,input);await ShowLead(y.Id,sid);}else{await api.SaveLeadAsync(id!.Value,input);await ShowLead(id,sid);}})));
        if(!fresh)v.Children.Add(B("מחק ליד","DeleteLeadButton",async(_,_)=>await Safe(async()=>{if(MessageBox.Show("למחוק את הליד?","אישור מחיקה",MessageBoxButton.YesNo)==MessageBoxResult.Yes){await api.DeleteLeadAsync(id!.Value);await ShowSite(sid,"leads");}})));
        ScreenHost.Content=S(v);
    }
    static DataGrid GridFor<T>(IEnumerable<T> items,string[] fields,string id){var g=new DataGrid{ItemsSource=items,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,Margin=new(4),MinHeight=180,FlowDirection=FlowDirection.RightToLeft,HorizontalContentAlignment=HorizontalAlignment.Right};AutomationProperties.SetAutomationId(g,id);foreach(var f in fields)g.Columns.Add(new DataGridTextColumn{Header=f,Binding=new Binding(f),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});return g;}
}
