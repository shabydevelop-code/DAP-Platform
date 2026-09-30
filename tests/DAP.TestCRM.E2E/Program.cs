using Microsoft.Playwright;

const string baseUrl = "http://localhost:5200";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = false, SlowMo = 180 });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 820 } });
await page.AddInitScriptAsync(@"(() => {
  const install=()=>{
    if(document.getElementById('dap-e2e-cursor')) return;
    const c=document.createElement('div'); c.id='dap-e2e-cursor';
    Object.assign(c.style,{position:'fixed',left:'0',top:'0',width:'18px',height:'18px',border:'2px solid #111',borderRadius:'50%',background:'rgba(255,255,255,.8)',zIndex:'2147483647',pointerEvents:'none',transform:'translate(-50%,-50%)',transition:'left .12s linear, top .12s linear'});
    document.documentElement.appendChild(c);
    document.addEventListener('mousemove',e=>{c.style.left=e.clientX+'px';c.style.top=e.clientY+'px'},true);
    document.addEventListener('mousedown',()=>{c.style.transform='translate(-50%,-50%) scale(.7)'},true);
    document.addEventListener('mouseup',()=>{c.style.transform='translate(-50%,-50%) scale(1)'},true);
  };
  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',install); else install();
})()");

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
async Task HumanPause(int ms=320) => await page.WaitForTimeoutAsync(ms);
async Task MoveTo(ILocator target)
{
    await target.ScrollIntoViewIfNeededAsync();
    await target.HoverAsync(new() { Position = new() { X = 12, Y = 12 } });
    await HumanPause();
}
async Task Click(string selector)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target); await target.ClickAsync(); await HumanPause(420);
}
async Task Fill(string selector,string value)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target); await target.ClickAsync();
    await page.Keyboard.PressAsync("Control+A");
    await page.Keyboard.TypeAsync(value,new() { Delay = 75 });
    await HumanPause();
}
async Task Select(string selector,string value)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target); await target.ClickAsync();
    await target.SelectOptionAsync(value);
    await HumanPause(650); await WaitReady();
}
async Task HumanScrollTo(ILocator target)
{
    for(var i=0;i<8 && !await target.IsVisibleAsync();i++){await page.Mouse.WheelAsync(0,240);await HumanPause(120);}
    await target.ScrollIntoViewIfNeededAsync(); await HumanPause();
}
async Task SaveExpectValidation(string field)
{
    await Click("button.primary:has-text('שמור')");
    await WaitReady();
    var f=await Content();
    await f.Locator($"[name='{field}'].validation-error").WaitForAsync();
    var ok=f.Locator("#ps-alert button"); await MoveTo(ok); await ok.ClickAsync(); await HumanPause();
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
await Fill("[name='resolutionNotes']","בוצעה בדיקת שירות מול הלקוח והתקלה טופלה.");

// 4. Real off-screen target / scrolling through activity history.
var more=frame.Locator("#activity-more");
await HumanScrollTo(more);
await MoveTo(more);
await more.ClickAsync();

// 5. Conditional target appears and moves layout.
await Select("[name='status']","סגורה");
frame=await Content();
// Case status FieldChange intentionally clears Subject. The agent re-enters it
// before exercising the Close Reason validation scenario.
await Fill("[name='subject']","תקלה בחיבור לאינטרנט");
frame=await Content();
await frame.Locator("[name='closeReason']").WaitForAsync();

// 6. Server validation changes layout, highlights field and opens modal.
await SaveExpectValidation("closeReason");
await Select("[name='closeReason']","טופל");
await SaveSuccess();

// 7. Continue legitimate agent work into Leads.
frame=await Content();
// The second breadcrumb link is the site and already points to the Cases tab.
// Navigate directly to the site's Leads tab using the stable site id encoded in that href.
var siteCrumb=frame.Locator(".breadcrumb a").Nth(1);
var siteHref=await siteCrumb.GetAttributeAsync("href") ?? throw new Exception("Site breadcrumb href missing.");
var marker="#/site/"; var pos=siteHref.IndexOf(marker,StringComparison.Ordinal);
if(pos<0) throw new Exception("Unexpected site breadcrumb href: "+siteHref);
var tail=siteHref[(pos+marker.Length)..];
var siteId=tail.Split('/')[0];
await MoveTo(siteCrumb);
await page.EvaluateAsync("(hash) => document.querySelector('#content-frame').contentWindow.location.hash = hash", $"#/site/{siteId}/leads");
await page.WaitForTimeoutAsync(700);
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
