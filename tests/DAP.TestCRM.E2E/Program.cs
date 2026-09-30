using Microsoft.Playwright;

const string baseUrl = "http://localhost:5200";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = false, SlowMo = 180 });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 820 } });
await page.AddInitScriptAsync(@"(() => {
  const install=()=>{
    if(window !== window.top) return;
    if(document.getElementById('dap-e2e-cursor')) return;
    const c=document.createElement('div'); c.id='dap-e2e-cursor';
    c.innerHTML='<svg width=""24"" height=""32"" viewBox=""0 0 24 32"" xmlns=""http://www.w3.org/2000/svg""><path d=""M2 2 L2 25 L8 19 L13 30 L17 28 L12 17 L21 17 Z"" fill=""white"" stroke=""#111"" stroke-width=""1.7"" stroke-linejoin=""round""/></svg>';
    Object.assign(c.style,{position:'fixed',left:'24px',top:'24px',width:'24px',height:'32px',zIndex:'2147483647',pointerEvents:'none',transition:'left .22s ease-out, top .22s ease-out, transform .08s ease-out',filter:'drop-shadow(1px 2px 1px rgba(0,0,0,.25))'});
    document.documentElement.appendChild(c);
    window.__dapE2ECursor={
      move:(x,y)=>{c.style.left=x+'px';c.style.top=y+'px'},
      down:()=>{c.style.transform='scale(.82)'},
      up:()=>{c.style.transform='scale(1)'}
    };
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
double cursorX=24,cursorY=24;
async Task MoveTo(ILocator target)
{
    await target.ScrollIntoViewIfNeededAsync();
    var box=await target.BoundingBoxAsync() ?? throw new Exception("Target has no bounding box.");
    // Playwright returns frame-element coordinates relative to the main viewport.
    // The visual cursor lives only in the top-level document.
    var x=box.X+Math.Min(14,box.Width/2);
    var y=box.Y+Math.Min(12,box.Height/2);
    const int steps=14;
    for(var i=1;i<=steps;i++)
    {
        var t=(double)i/steps;
        var sx=cursorX+(x-cursorX)*t;
        var sy=cursorY+(y-cursorY)*t;
        await page.EvaluateAsync("(p)=>window.__dapE2ECursor?.move(p.x,p.y)",new { x=sx,y=sy });
        await page.WaitForTimeoutAsync(35);
    }
    cursorX=x; cursorY=y;
    await target.HoverAsync(new() { Position = new() { X = Math.Min(14,box.Width/2), Y = Math.Min(12,box.Height/2) } });
    await HumanPause(120);
}
async Task Click(string selector)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target);
    await page.EvaluateAsync("()=>window.__dapE2ECursor?.down()");
    await target.ClickAsync();
    await page.EvaluateAsync("()=>window.__dapE2ECursor?.up()");
    await HumanPause(420);
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
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
var frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
await Click("th button[data-sort='status']");
await WaitReady();

// Create a fresh open Case so repeated runs never depend on mutated seed data.
await Click("button.primary:has-text('פניה חדשה')");
await WaitReady();
await Fill("[name='subject']","תקלה בחיבור לאינטרנט");
await Fill("[name='description']","הלקוח מדווח על חיבור לא יציב.");
await SaveSuccess();

// Return to the Cases grid, then open the freshly-created open Case through one
// of the repeated identical Open targets. This also replaces the content iframe.
frame=await Content();
var casesCrumb=frame.Locator(".breadcrumb a").Nth(1);
await MoveTo(casesCrumb); await casesCrumb.ClickAsync(); await WaitReady();
await Click("tbody tr:has-text('פתוחה') button.grid-open");
await WaitReady();

// 3. Case FieldChange: disabled -> enabled and DOM reconstruction.
frame=await Content();
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
await siteCrumb.ClickAsync();
await page.WaitForTimeoutAsync(500);
await WaitReady();
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
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
