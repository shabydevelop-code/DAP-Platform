using Microsoft.Playwright;

const string baseUrl = "http://localhost:5200";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = false, Args = new[] { "--start-maximized" } });
var context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport, ExtraHTTPHeaders = new Dictionary<string,string> { ["X-DAP-E2E-Mode"] = fastMode ? "fast" : "visual" } });
var page = await context.NewPageAsync();
page.SetDefaultTimeout(5000);

var e2eMode = Environment.GetEnvironmentVariable("DAP_E2E_MODE")?.Trim().ToLowerInvariant() ?? "fast";
var visualMode = e2eMode is "visual" or "demo";
var fastMode = !visualMode;

Console.WriteLine($"E2E mode: {(visualMode ? "visual" : "fast")}");
await page.AddInitScriptAsync(@"(() => {
  const install=()=>{
    if(window !== window.top) return;
    if(document.getElementById('dap-e2e-cursor')) return;
    const c=document.createElement('div'); c.id='dap-e2e-cursor';
    c.innerHTML='<svg width=""18"" height=""24"" viewBox=""0 0 24 32"" xmlns=""http://www.w3.org/2000/svg""><path d=""M2 2 L2 25 L8 19 L13 30 L17 28 L12 17 L21 17 Z"" fill=""#2F80ED"" stroke=""white"" stroke-width=""2"" stroke-linejoin=""round""/></svg>';
    Object.assign(c.style,{position:'fixed',left:'24px',top:'24px',width:'18px',height:'24px',zIndex:'2147483647',pointerEvents:'none',transition:'left .22s ease-out, top .22s ease-out, transform .08s ease-out',filter:'drop-shadow(1px 2px 1px rgba(0,0,0,.25))'});
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
    // Re-query the current DOM iframe on every attempt. A locator/element handle
    // captured before a PeopleSoft-style reload/replacement can point at a
    // retiring frame and must never be treated as the active content context.
    for(var i=0;i<100;i++)
    {
        try
        {
            var element=page.Locator("#content-frame");
            if(await element.CountAsync()==1)
            {
                var handle=await element.ElementHandleAsync();
                var frame=handle is null ? null : await handle.ContentFrameAsync();
                if(frame is not null && !frame.IsDetached &&
                   await frame.Locator("html[data-dap-ready='1']").CountAsync()>0)
                    return frame;
            }
        }
        catch(PlaywrightException) { }
        await page.WaitForTimeoutAsync(100);
    }
    throw new Exception("Stable content iframe not found.");
}
async Task WaitReady()
{
    var f = await Content();
    await f.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    await f.Locator("#server-busy").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10000 });
}
async Task HumanPause(int ms=320)
{
    if (visualMode) await page.WaitForTimeoutAsync(ms);
}
double cursorX=24,cursorY=24;
async Task MoveTo(ILocator target)
{
    await target.ScrollIntoViewIfNeededAsync();
    var box=await target.BoundingBoxAsync() ?? throw new Exception("Target has no bounding box.");
    var tag=await target.EvaluateAsync<string>("e=>e.tagName");
    var isSelect=tag=="SELECT";
    var localX=isSelect ? Math.Min(16,box.Width/2) : box.Width/2;
    var localY=box.Height/2;

    if (fastMode)
    {
        await target.HoverAsync(new() { Position = new() { X = localX, Y = localY } });
        return;
    }

    var x=box.X+localX;
    var y=box.Y+localY;
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
    await target.HoverAsync(new() { Position = new() { X = localX, Y = localY } });
    await HumanPause(120);
}
async Task Click(string selector)
{
    var f=await Content(); var target=f.Locator(selector);
    var replacesFrame=await target.GetAttributeAsync("data-frame-nav")=="replace";
    await MoveTo(target);
    await page.EvaluateAsync("()=>window.__dapE2ECursor?.down()");
    await target.ClickAsync();
    await page.EvaluateAsync("()=>window.__dapE2ECursor?.up()");

    if(replacesFrame)
    {
        // Do not let the next assertion bind to the still-visible retiring
        // iframe. Wait for the replacement lifecycle to start and finish.
        await page.Locator("#content-frame-next").WaitForAsync(new() {
            State = WaitForSelectorState.Attached, Timeout = 10000
        });
        await page.Locator("#content-frame-next").WaitForAsync(new() {
            State = WaitForSelectorState.Detached, Timeout = 10000
        });
    }
    await HumanPause(420);
}
async Task Fill(string selector,string value)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target); await target.ClickAsync();
    await page.Keyboard.PressAsync("Control+A");
    await page.Keyboard.TypeAsync(value,new() { Delay = visualMode ? 75 : 0 });
    await HumanPause();
}
async Task Select(string selector,string value)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target);
    if (visualMode) await HumanPause(300);

    // Keep the system test deterministic: select the real option directly.
    // SelectOption fires the real change event and therefore the real CRM FieldChange flow.
    await target.SelectOptionAsync(value);
    await HumanPause(800);
    await WaitReady();
}
async Task HumanScrollTo(ILocator target)
{
    // Scroll in small visible wheel steps. Do not jump directly to the target
    // unless the browser still needs a final minimal alignment.
    for(var i=0;i<18;i++)
    {
        var box=await target.BoundingBoxAsync();
        var viewport=page.ViewportSize;
        if(box is not null && viewport is not null && box.Y>=70 && box.Y+box.Height<=viewport.Height-35) break;
        await page.Mouse.WheelAsync(0,110);

        // Pause only when another wheel step is actually needed. Previously the
        // final wheel step always paid 180 ms before MoveTo could even begin.
        var after=await target.BoundingBoxAsync();
        var afterViewport=page.ViewportSize;
        var reached=after is not null && afterViewport is not null &&
                    after.Y>=70 && after.Y+after.Height<=afterViewport.Height-35;
        if(reached) break;
        await HumanPause(180);
    }
    if(!await target.IsVisibleAsync()) await target.ScrollIntoViewIfNeededAsync();
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

// Search always renders a result grid, including a single match.
await Click("#search-results tbody tr.clickable:first-child");
await WaitReady();
// Open first site.
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
frame=await Content();
var createdCaseUrl=frame.Url;
var caseMarker="#/case/";
var casePos=createdCaseUrl.IndexOf(caseMarker,StringComparison.Ordinal);
if(casePos<0) throw new Exception("Created Case id missing from route: "+createdCaseUrl);
var createdCaseId=createdCaseUrl[(casePos+caseMarker.Length)..].Split('?', '/', '#')[0];

// Return to the Cases grid, then open exactly the Case created by this run.
// This also verifies repeated identical Open targets without relying on unique status text.
frame=await Content();
var casesCrumb=frame.Locator(".breadcrumb a").Nth(2);
await MoveTo(casesCrumb); await casesCrumb.ClickAsync(); await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");
await WaitReady();

// 3. Case FieldChange: disabled -> enabled and DOM reconstruction.
// Reacquire only after the promoted replacement frame reports app-level readiness.
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
// On a Case page the breadcrumb is Portal -> Customer -> Site -> Case.
// Use the actual Site breadcrumb (Nth(2)); data-go is the navigation contract,
// while href is intentionally absent because these are app-controlled anchors.
var siteCrumb=frame.Locator(".breadcrumb a").Nth(2);
var siteRoute=await siteCrumb.GetAttributeAsync("data-go") ?? throw new Exception("Site breadcrumb route missing.");
if(!siteRoute.StartsWith("#/site/",StringComparison.Ordinal)) throw new Exception("Unexpected site breadcrumb route: "+siteRoute);
await MoveTo(siteCrumb);
await siteCrumb.ClickAsync();
await page.WaitForTimeoutAsync(visualMode ? 500 : 0);
await WaitReady();
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();

// Create a Lead in this run. After the first save the same workflow changes
// from "new" to a persisted Lead and the Delete button is rendered dynamically.
await Click("button.primary:has-text('ליד חדש')");
await WaitReady();
await Fill("[name='contactName']","לקוח בדיקת מערכת");
await SaveSuccess();
frame=await Content();
var dynamicDeleteLead=frame.Locator("#delete-lead");
await dynamicDeleteLead.WaitForAsync();

// 8. Lead server FieldChange + conditional required business field.
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
await SaveExpectValidation("selectedService");
await Select("[name='selectedService']","תמיכה מורחבת");
await SaveSuccess();

// Exercise the dynamically rendered Delete target in the same Lead context:
// no navigation away and no reopening of the record.
frame=await Content();
dynamicDeleteLead=frame.Locator("#delete-lead");
await MoveTo(dynamicDeleteLead);
await dynamicDeleteLead.ClickAsync();
var confirmDeleteLead=frame.Locator("#ps-confirm [data-answer='yes']");
await confirmDeleteLead.WaitForAsync();
await MoveTo(confirmDeleteLead);
await confirmDeleteLead.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();

// 9. Delete the Case created by this run through the real UI.
// We are already on the Site's Leads tab after Lead deletion, so switch tabs
// directly. The Site name on a Site page is plain breadcrumb text, not a link.
frame=await Content();
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");
await WaitReady();
await Click("#delete-case");
frame=await Content();
var confirmDelete=frame.Locator("#ps-confirm [data-answer='yes']");
await confirmDelete.WaitForAsync();
await MoveTo(confirmDelete);
await confirmDelete.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
if(await frame.Locator($"button.grid-open[data-go='#/case/{createdCaseId}']").CountAsync()!=0)
    throw new Exception($"Deleted Case {createdCaseId} is still present in the Cases grid.");

Console.WriteLine("PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.");
await page.WaitForTimeoutAsync(visualMode ? 1500 : 0);
