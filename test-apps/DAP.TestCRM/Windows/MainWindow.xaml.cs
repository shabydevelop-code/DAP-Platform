using System.Windows;
using System.Windows.Controls;

namespace DAP.TestCRM.Windows;

public partial class MainWindow : Window
{
    private readonly CrmApiClient _api = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadCustomersAsync();
    }

    private async Task ReloadCustomersAsync()
    {
        try
        {
            StatusText.Text = "טוען לקוחות...";
            var customers = await _api.GetCustomersAsync();
            CustomersGrid.ItemsSource = customers;
            if (customers.Count > 0) CustomersGrid.SelectedIndex = 0;
            StatusText.Text = "מחובר לשרת TestCRM";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReloadCustomersAsync();

    private async void CustomersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomersGrid.SelectedItem is not Customer customer) return;
        try
        {
            var sites = await _api.GetSitesAsync(customer.Id);
            SitesGrid.ItemsSource = sites;
            CasesGrid.ItemsSource = null;
            LeadsGrid.ItemsSource = null;
            if (sites.Count > 0) SitesGrid.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void SitesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SitesGrid.SelectedItem is not Site site) return;
        SiteNameBox.Text = site.Name;
        SelectCombo(SiteTypeBox, site.Type);
        SiteAddressBox.Text = site.Address;
        try
        {
            var casesTask = _api.GetCasesAsync(site.Id);
            var leadsTask = _api.GetLeadsAsync(site.Id);
            await Task.WhenAll(casesTask, leadsTask);
            CasesGrid.ItemsSource = casesTask.Result;
            LeadsGrid.ItemsSource = leadsTask.Result;
            if (casesTask.Result.Count > 0) CasesGrid.SelectedIndex = 0;
            if (leadsTask.Result.Count > 0) LeadsGrid.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void CasesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CasesGrid.SelectedItem is not CaseItem item) return;
        SelectCombo(CaseStatusBox, item.Status);
        CaseSubjectBox.Text = item.Subject;
        CaseDescriptionBox.Text = item.Description;
        CaseCloseReasonBox.Text = item.CloseReason;
    }

    private void LeadsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LeadsGrid.SelectedItem is not Lead item) return;
        SelectCombo(LeadSourceBox, item.Source);
        LeadContactBox.Text = item.ContactName;
        SelectCombo(LeadStatusBox, item.Status);
        LeadNotesBox.Text = item.Notes;
        LeadSelectedServiceBox.Text = item.SelectedService;
        LeadLostReasonBox.Text = item.LostReason;
    }

    private async void SaveSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SitesGrid.SelectedItem is not Site site) return;
        await RunSaveAsync(async () =>
        {
            await _api.SaveSiteAsync(site.Id, new(SiteNameBox.Text, ComboText(SiteTypeBox), SiteAddressBox.Text));
            await ReloadSelectedCustomerAsync(site.Id);
        }, "האתר נשמר.");
    }

    private async void SaveCaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (CasesGrid.SelectedItem is not CaseItem item || SitesGrid.SelectedItem is not Site site) return;
        await RunSaveAsync(async () =>
        {
            await _api.SaveCaseAsync(item.Id, new(ComboText(CaseStatusBox), CaseSubjectBox.Text, CaseDescriptionBox.Text, CaseCloseReasonBox.Text));
            await ReloadSiteChildrenAsync(site.Id, item.Id, null);
        }, "הפנייה נשמרה.");
    }

    private async void SaveLeadButton_Click(object sender, RoutedEventArgs e)
    {
        if (LeadsGrid.SelectedItem is not Lead item || SitesGrid.SelectedItem is not Site site) return;
        await RunSaveAsync(async () =>
        {
            await _api.SaveLeadAsync(item.Id, new(ComboText(LeadSourceBox), LeadContactBox.Text, ComboText(LeadStatusBox), LeadNotesBox.Text, LeadSelectedServiceBox.Text, LeadLostReasonBox.Text));
            await ReloadSiteChildrenAsync(site.Id, null, item.Id);
        }, "הליד נשמר.");
    }

    private async Task ReloadSelectedCustomerAsync(int siteId)
    {
        if (CustomersGrid.SelectedItem is not Customer customer) return;
        var sites = await _api.GetSitesAsync(customer.Id);
        SitesGrid.ItemsSource = sites;
        SitesGrid.SelectedItem = sites.FirstOrDefault(x => x.Id == siteId);
    }

    private async Task ReloadSiteChildrenAsync(int siteId, int? caseId, int? leadId)
    {
        var cases = await _api.GetCasesAsync(siteId);
        var leads = await _api.GetLeadsAsync(siteId);
        CasesGrid.ItemsSource = cases;
        LeadsGrid.ItemsSource = leads;
        if (caseId.HasValue) CasesGrid.SelectedItem = cases.FirstOrDefault(x => x.Id == caseId.Value);
        if (leadId.HasValue) LeadsGrid.SelectedItem = leads.FirstOrDefault(x => x.Id == leadId.Value);
    }

    private async Task RunSaveAsync(Func<Task> action, string success)
    {
        try { StatusText.Text = "שומר..."; await action(); StatusText.Text = success; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex)
    {
        StatusText.Text = "שגיאה";
        MessageBox.Show(this, ex.Message, "DAP Test CRM", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string ComboText(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static void SelectCombo(ComboBox box, string value)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Content?.ToString(), value, StringComparison.Ordinal));
    }
}
