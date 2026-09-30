using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var customers = new ConcurrentDictionary<int, Customer>();
var sites = new ConcurrentDictionary<int, Site>();
var cases = new ConcurrentDictionary<int, Case>();
var leads = new ConcurrentDictionary<int, Lead>();

customers[1] = new(1, "Northwind Israel", "03-5550100", "contact@northwind.test");
customers[2] = new(2, "Contoso Services", "03-5550200", "office@contoso.test");
sites[1] = new(1, 1, "Tel Aviv HQ", "Office", "HaArba'a 10, Tel Aviv");
sites[2] = new(2, 1, "Haifa Branch", "Branch", "HaNamal 20, Haifa");
sites[3] = new(3, 2, "Jerusalem Office", "Office", "Jaffa 50, Jerusalem");
cases[1] = new(1, 1, "Open", "Internet connectivity", "Connection is unstable");
cases[2] = new(2, 1, "Closed", "Access card", "Replacement completed");
cases[3] = new(3, 2, "Open", "Printer", "Printer is unavailable");
leads[1] = new(1, 1, "Website", "Dana Levi", "New", "Requested product information");
leads[2] = new(2, 1, "Referral", "Avi Cohen", "Qualified", "Follow-up scheduled");
leads[3] = new(3, 2, "Campaign", "Noa Bar", "New", "Interested in upgrade");

app.MapGet("/api/customers", () => customers.Values.OrderBy(x => x.Id));
app.MapGet("/api/customers/{id:int}", (int id) => customers.TryGetValue(id, out var x) ? Results.Ok(x) : Results.NotFound());
app.MapGet("/api/customers/{id:int}/sites", (int id) => sites.Values.Where(x => x.CustomerId == id).OrderBy(x => x.Id));

app.MapGet("/api/sites/{id:int}", (int id) => sites.TryGetValue(id, out var x) ? Results.Ok(x) : Results.NotFound());
app.MapPost("/api/customers/{customerId:int}/sites", (int customerId, SiteInput input) =>
{
    if (!customers.ContainsKey(customerId)) return Results.NotFound();
    var id = sites.Keys.DefaultIfEmpty().Max() + 1;
    var site = new Site(id, customerId, input.Name, input.Type, input.Address);
    sites[id] = site;
    return Results.Created($"/api/sites/{id}", site);
});
app.MapPut("/api/sites/{id:int}", async (int id, SiteInput input) =>
{
    if (!sites.TryGetValue(id, out var old)) return Results.NotFound();
    await Task.Delay(450); // deliberate server round-trip for DAP runtime testing
    var updated = old with { Name = input.Name, Type = input.Type, Address = input.Address };
    sites[id] = updated;
    return Results.Ok(updated);
});

app.MapGet("/api/sites/{siteId:int}/cases", (int siteId) => cases.Values.Where(x => x.SiteId == siteId).OrderByDescending(x => x.Id));
app.MapGet("/api/cases/{id:int}", (int id) => cases.TryGetValue(id, out var x) ? Results.Ok(x) : Results.NotFound());
app.MapPost("/api/sites/{siteId:int}/cases", (int siteId, CaseInput input) =>
{
    if (!sites.ContainsKey(siteId)) return Results.NotFound();
    var id = cases.Keys.DefaultIfEmpty().Max() + 1;
    var item = new Case(id, siteId, input.Status, input.Subject, input.Description);
    cases[id] = item;
    return Results.Created($"/api/cases/{id}", item);
});
app.MapPut("/api/cases/{id:int}", (int id, CaseInput input) =>
{
    if (!cases.TryGetValue(id, out var old)) return Results.NotFound();
    var item = old with { Status = input.Status, Subject = input.Subject, Description = input.Description };
    cases[id] = item;
    return Results.Ok(item);
});

app.MapGet("/api/sites/{siteId:int}/leads", (int siteId) => leads.Values.Where(x => x.SiteId == siteId).OrderByDescending(x => x.Id));
app.MapGet("/api/leads/{id:int}", (int id) => leads.TryGetValue(id, out var x) ? Results.Ok(x) : Results.NotFound());
app.MapPost("/api/sites/{siteId:int}/leads", (int siteId, LeadInput input) =>
{
    if (!sites.ContainsKey(siteId)) return Results.NotFound();
    var id = leads.Keys.DefaultIfEmpty().Max() + 1;
    var item = new Lead(id, siteId, input.Source, input.ContactName, input.Status, input.Notes);
    leads[id] = item;
    return Results.Created($"/api/leads/{id}", item);
});
app.MapPut("/api/leads/{id:int}", (int id, LeadInput input) =>
{
    if (!leads.TryGetValue(id, out var old)) return Results.NotFound();
    var item = old with { Source = input.Source, ContactName = input.ContactName, Status = input.Status, Notes = input.Notes };
    leads[id] = item;
    return Results.Ok(item);
});

app.Run();

record Customer(int Id, string Name, string Phone, string Email);
record Site(int Id, int CustomerId, string Name, string Type, string Address);
record Case(int Id, int SiteId, string Status, string Subject, string Description);
record Lead(int Id, int SiteId, string Source, string ContactName, string Status, string Notes);
record SiteInput(string Name, string Type, string Address);
record CaseInput(string Status, string Subject, string Description);
record LeadInput(string Source, string ContactName, string Status, string Notes);
