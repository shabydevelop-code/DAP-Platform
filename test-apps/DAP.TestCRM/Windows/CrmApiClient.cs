using System.Net.Http.Json;
using System.Text.Json;

namespace DAP.TestCRM.Windows;

public sealed class CrmApiClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5200") };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<Customer>> GetCustomersAsync() =>
        await _http.GetFromJsonAsync<List<Customer>>("/api/customers", JsonOptions) ?? [];

    public async Task<List<Site>> GetSitesAsync(int customerId) =>
        await _http.GetFromJsonAsync<List<Site>>($"/api/customers/{customerId}/sites", JsonOptions) ?? [];

    public async Task<List<CaseItem>> GetCasesAsync(int siteId) =>
        await _http.GetFromJsonAsync<List<CaseItem>>($"/api/sites/{siteId}/cases", JsonOptions) ?? [];

    public async Task<List<Lead>> GetLeadsAsync(int siteId) =>
        await _http.GetFromJsonAsync<List<Lead>>($"/api/sites/{siteId}/leads", JsonOptions) ?? [];

    public async Task SaveSiteAsync(int id, SiteInput input) => await SendAsync(HttpMethod.Put, $"/api/sites/{id}", input);
    public async Task SaveCaseAsync(int id, CaseInput input) => await SendAsync(HttpMethod.Put, $"/api/cases/{id}", input);
    public async Task SaveLeadAsync(int id, LeadInput input) => await SendAsync(HttpMethod.Put, $"/api/leads/{id}", input);

    private async Task SendAsync<T>(HttpMethod method, string path, T body)
    {
        using var response = await _http.SendAsync(new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) });
        if (response.IsSuccessStatusCode) return;

        var text = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(text)
            ? $"CRM API returned {(int)response.StatusCode}."
            : text);
    }
}
