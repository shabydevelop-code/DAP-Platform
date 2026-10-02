using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace DAP.TestCRM.Windows;

public sealed class CrmApiClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5201") };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<List<Customer>> GetCustomersAsync(string? name=null,string? phone=null,string? email=null) =>
        GetListAsync<Customer>("/api/customers"+Query(("name",name),("phone",phone),("email",email)));
    public Task<Customer?> GetCustomerAsync(int id) => GetAsync<Customer>($"/api/customers/{id}");
    public Task<List<Site>> GetSitesAsync(int customerId) => GetListAsync<Site>($"/api/customers/{customerId}/sites");
    public Task<Site?> GetSiteAsync(int id) => GetAsync<Site>($"/api/sites/{id}");
    public Task<List<CaseItem>> GetCasesAsync(int siteId,string? sort=null,string? dir=null) =>
        GetListAsync<CaseItem>($"/api/sites/{siteId}/cases"+Query(("sort",sort),("dir",dir)));
    public Task<CaseItem?> GetCaseAsync(int id) => GetAsync<CaseItem>($"/api/cases/{id}");
    public Task<List<Lead>> GetLeadsAsync(int siteId) => GetListAsync<Lead>($"/api/sites/{siteId}/leads");
    public Task<Lead?> GetLeadAsync(int id) => GetAsync<Lead>($"/api/leads/{id}");

    public Task<Customer> CreateCustomerAsync(string name,string phone,string email) => PostAsync<Customer>("/api/customers",new {name,phone,email});
    public Task<Site> CreateSiteAsync(int customerId,SiteInput x) => PostAsync<Site>($"/api/customers/{customerId}/sites",x);
    public Task<CaseItem> CreateCaseAsync(int siteId,CaseInput x) => PostAsync<CaseItem>($"/api/sites/{siteId}/cases",x);
    public Task<Lead> CreateLeadAsync(int siteId,LeadInput x) => PostAsync<Lead>($"/api/sites/{siteId}/leads",x);
    public Task SaveSiteAsync(int id,SiteInput x) => SendAsync(HttpMethod.Put,$"/api/sites/{id}",x);
    public Task SaveCaseAsync(int id,CaseInput x) => SendAsync(HttpMethod.Put,$"/api/cases/{id}",x);
    public Task SaveLeadAsync(int id,LeadInput x) => SendAsync(HttpMethod.Put,$"/api/leads/{id}",x);
    public Task DeleteSiteAsync(int id) => SendAsync(HttpMethod.Delete,$"/api/sites/{id}",new {});
    public Task DeleteCaseAsync(int id) => SendAsync(HttpMethod.Delete,$"/api/cases/{id}",new {});
    public Task DeleteLeadAsync(int id) => SendAsync(HttpMethod.Delete,$"/api/leads/{id}",new {});
    public Task<CaseStatusFieldChange> CaseStatusChangedAsync(int id,string status) => PostAsync<CaseStatusFieldChange>($"/api/cases/{id}/fieldchange/status",new {status});
    public Task<LeadStatusFieldChange> LeadStatusChangedAsync(int id,string status) => PostAsync<LeadStatusFieldChange>($"/api/leads/{id}/fieldchange/status",new {status});

    private async Task<T?> GetAsync<T>(string path) => await _http.GetFromJsonAsync<T>(path,JsonOptions);
    private async Task<List<T>> GetListAsync<T>(string path) => await _http.GetFromJsonAsync<List<T>>(path,JsonOptions) ?? [];
    private async Task<T> PostAsync<T>(string path,object body)
    {
        using var r=await _http.PostAsJsonAsync(path,body);
        await EnsureAsync(r);
        return (await r.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
    private async Task SendAsync<T>(HttpMethod method,string path,T body)
    {
        using var r=await _http.SendAsync(new HttpRequestMessage(method,path){Content=JsonContent.Create(body)});
        await EnsureAsync(r);
    }
    private static async Task EnsureAsync(HttpResponseMessage r)
    {
        if(r.IsSuccessStatusCode)return;
        var body=await r.Content.ReadAsStringAsync();
        var message=TryGetProblemMessage(body);
        throw new InvalidOperationException(message ?? (string.IsNullOrWhiteSpace(body)?$"CRM API returned {(int)r.StatusCode}.":body));
    }

    private static string? TryGetProblemMessage(string body)
    {
        if(string.IsNullOrWhiteSpace(body))return null;
        try
        {
            using var doc=JsonDocument.Parse(body);
            var root=doc.RootElement;
            if(root.TryGetProperty("errors",out var errors) && errors.ValueKind==JsonValueKind.Object)
            {
                var messages=new List<string>();
                foreach(var property in errors.EnumerateObject())
                {
                    if(property.Value.ValueKind==JsonValueKind.Array)
                        foreach(var item in property.Value.EnumerateArray())
                            if(item.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                                messages.Add(item.GetString()!);
                    else if(property.Value.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                        messages.Add(property.Value.GetString()!);
                }
                if(messages.Count>0)return string.Join(Environment.NewLine,messages.Distinct());
            }
            if(root.TryGetProperty("detail",out var detail) && detail.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(detail.GetString()))
                return detail.GetString();
            if(root.TryGetProperty("title",out var title) && title.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString()))
                return title.GetString();
        }
        catch(JsonException)
        {
        }
        return null;
    }
    private static string Query(params (string key,string? value)[] values)
    {
        var q=values.Where(x=>!string.IsNullOrWhiteSpace(x.value)).Select(x=>$"{x.key}={Uri.EscapeDataString(x.value!.Trim())}").ToArray();
        return q.Length==0?"":"?"+string.Join("&",q);
    }
}
