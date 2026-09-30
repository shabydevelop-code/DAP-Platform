using Microsoft.Playwright;

const string baseUrl = "http://localhost:5200";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = false, SlowMo = 180 });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 820 } });

async Task<IFrame> Content()
{
    await page.WaitForSelectorAsync("#content-frame");
    var frame = page.Frames.FirstOrDefault(f => f.Name == "dap-content");
    if (frame is null) throw new Exception("Content iframe not found.");
    return frame;
}
async Task WaitReady()
{
    var f = await Content();
    await f.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    await f.Locator("#server-busy").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10000 });
}
async Task Click(string selector) { var f=await Content(); await f.Locator(selector).ClickAsync(); await page.WaitForTimeoutAsync(250); }
async Task Fill(string selector,string value) { var f=await Content(); await f.Locator(selector).FillAsync(value); }
async Task Select(string selector,string value) { var f=await Content(); await f.Locator(selector).SelectOptionAsync(value); await page.WaitForTimeoutAsync(900); await WaitReady(); }
async Task SaveExpectValidation(string field)
{
    await Click("button.primary:has-text('שמור')");
    await WaitReady();
    var f=await Content();
    await f.Locator($"[name='{field}'].validation-error").WaitForAsync();
    await f.Locator("#ps-alert button").ClickAsync();
}
async Task SaveSuccess()
{
    await Click("button.primary:has-text('שמור')");
    await WaitReady();
    var f=await Content();
    await f.Locator("#save-success").WaitForAsync();
}

Console.WriteLine("DAP TestCRM representative PeopleSoft-Web scenario");
await page.GotoAsync(baseUrl);
await WaitReady();

// 1. Legitimate customer lookup: server round trip + working context.
await Fill("[name='name']","אלפא");
await Click("#customer-search button.primary");
await WaitReady();

// One-result search navigates to customer. Open first site.
await Click("tbody tr.clickable:first-child");
await WaitReady();

// 2. Cases grid: server sorting + repeated identical Open targets.
await Click("button[data-go$='/cases']");
await WaitReady();
await Click("th button[data-sort='status']");
await WaitReady();

// Opening through repeated Open buttons replaces the content iframe itself.
await Click("tbody tr:has-text('פתוחה') button.grid-open");
await WaitReady();

// 3. Case FieldChange: disabled -> enabled and DOM reconstruction.
var frame=await Content();
var notes=frame.Locator("[name='resolutionNotes']");
if(!await notes.IsDisabledAsync()) throw new Exception("Treatment Notes should start disabled for an open case.");
await Select("[name='status']","בטיפול");
frame=await Content(); notes=frame.Locator("[name='resolutionNotes']");
if(await notes.IsDisabledAsync()) throw new Exception("Treatment Notes did not become enabled.");
await notes.FillAsync("בוצעה בדיקת שירות מול הלקוח והתקלה טופלה.");

// 4. Real off-screen target / scrolling through activity history.
var more=frame.Locator("#activity-more");
await more.ScrollIntoViewIfNeededAsync();
await more.ClickAsync();

// 5. Conditional target appears and moves layout.
await Select("[name='status']","סגורה");
frame=await Content();
await frame.Locator("[name='closeReason']").WaitForAsync();

// 6. Server validation changes layout, highlights field and opens modal.
await SaveExpectValidation("closeReason");
await Select("[name='closeReason']","טופל");
await SaveSuccess();

// 7. Continue legitimate agent work into Leads.
frame=await Content();
await frame.Locator(".breadcrumb a").Nth(1).ClickAsync();
await page.WaitForTimeoutAsync(500);
await Click("button[data-go$='/leads']");
await WaitReady();
await Click("tbody tr.clickable:first-child");
await WaitReady();

// 8. Lead server FieldChange + conditional required business field.
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
await SaveExpectValidation("selectedService");
await Select("[name='selectedService']","תמיכה מורחבת");
await SaveSuccess();

Console.WriteLine("PASS: representative Customer -> Site -> Case -> Lead workflow completed.");
await page.WaitForTimeoutAsync(1500);
