using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DAP.TestCRM.E2E;
using Microsoft.Playwright;
using DAP.Core.Targets;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

const string baseUrl = "http://localhost:5200";
static int ReserveTcpPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

var harnessStartupTimer=Stopwatch.StartNew();
var harnessLastMark=TimeSpan.Zero;
void StartupMark(string stage)
{
    var now=harnessStartupTimer.Elapsed;
    Console.WriteLine($"[E2E startup] {now.TotalMilliseconds:F0} ms total (+{(now-harnessLastMark).TotalMilliseconds:F0} ms) - {stage}");
    harnessLastMark=now;
}

var dapCdpPort = ReserveTcpPort();
StartupMark("CDP port reserved");
using var playwright = await Playwright.CreateAsync();
StartupMark("Playwright created");
await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Headless = false,
    Args = new[] { "--start-maximized", $"--remote-debugging-port={dapCdpPort}" }
});
StartupMark("Chromium launched");
var context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport, ExtraHTTPHeaders = new Dictionary<string,string> { ["X-DAP-E2E-Mode"] = (Environment.GetEnvironmentVariable("DAP_E2E_MODE")?.Trim().ToLowerInvariant() ?? "fast") } });
var page = await context.NewPageAsync();
page.SetDefaultTimeout(5000);
StartupMark("browser context and page created");

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

await page.AddInitScriptAsync("localStorage.setItem('dap-e2e-mode', '" + e2eMode + "'); document.documentElement.dataset.dapE2eMode = '" + e2eMode + "';");

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
Console.WriteLine("Scenario 1: Case status FieldChange + Content iframe replacement");
Console.WriteLine("Scenario 2: Case validation failure + preservation of unsaved values");
Console.WriteLine("Scenario 3: Grid rerender/reorder + target re-resolution");
Console.WriteLine("Scenario 4: Full page reload + business context preservation");
Console.WriteLine("Scenario 5: CRM tab switching + business context preservation");
Console.WriteLine("Scenario 6: Conditional target disappearance/reappearance + re-resolution");
Console.WriteLine("Scenario 7: Cross-frame navigation from Header to Content");
Console.WriteLine("Scenario 8: Layout shift + target re-resolution");
Console.WriteLine("Scenario 9: Consecutive server updates + final-state re-resolution");
Console.WriteLine("Scenario 10: Business context switch + target isolation");
StartupMark("scenario harness initialized");
await page.GotoAsync(baseUrl);
StartupMark("TestCRM navigation completed");
await WaitReady();
StartupMark("TestCRM ready");

// First real DAP Web bubble: persist -> reload -> resolve -> present.
var dapDbPath=Path.Combine(Path.GetTempPath(),"DAP.TestCRM.E2E",Guid.NewGuid()+".db");
var dapFactory=new SqliteConnectionFactory(new SqliteDatabaseOptions(dapDbPath));
await new SqliteDatabaseInitializer(dapFactory).InitializeAsync();
var dapRepository=new SqliteGuideStepRepository(dapFactory);
foreach(var guideStep in DapTestCrmGuideSeed.Steps)
    await dapRepository.SaveStepAsync(DapTestCrmGuideSeed.GuideId,guideStep);
var dapSteps=await dapRepository.GetStepsAsync(DapTestCrmGuideSeed.GuideId);
StartupMark("temporary DAP guide database seeded");
if(dapSteps.Count!=DapTestCrmGuideSeed.Steps.Count)
    throw new Exception($"Expected {DapTestCrmGuideSeed.Steps.Count} persisted DAP Steps, found {dapSteps.Count}.");
var dapStep=dapSteps[0];
var dapSecondStep=dapSteps[1];

async Task WaitForGuideStep(int order)
{
    var expected=dapSteps.Single(step=>step.Order==order);
    for(var i=0;i<100;i++)
    {
        var content=await Content();
        var bubble=content.Locator("#dap-guide-bubble");
        if(await bubble.CountAsync()==1 && await bubble.TextContentAsync()==expected.Bubble.Content)
            return;
        await page.WaitForTimeoutAsync(100);
    }
    throw new TimeoutException($"DAP Guide did not present Step {order}: {expected.Id}.");
}

var dapAppProject=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","src","DAP.App","DAP.App.csproj"));
if(!File.Exists(dapAppProject))
    throw new Exception($"DAP.App project not found at {dapAppProject}");

// Build into an E2E-owned output directory. A DAP process orphaned by an
// interrupted earlier run can then only lock its own old output, never this run.
var dapBuildOutput=Path.Combine(Path.GetTempPath(),"DAP","E2E","app");
Directory.CreateDirectory(dapBuildOutput);
var dapExecutable=Path.Combine(dapBuildOutput,"DAP.exe");

// Ctrl+C or a hard parent-process termination can leave the E2E-owned DAP
// process alive. Before rebuilding the stable output, remove only an orphan
// whose executable path is exactly this harness-owned DAP.exe. Never kill
// unrelated product DAP processes.
foreach(var candidate in Process.GetProcessesByName("DAP"))
{
    using(candidate)
    {
        try
        {
            var candidatePath=candidate.MainModule?.FileName;
            if(!string.Equals(
                Path.GetFullPath(candidatePath ?? string.Empty),
                Path.GetFullPath(dapExecutable),
                StringComparison.OrdinalIgnoreCase))
                continue;

            Console.WriteLine($"Cleaning orphaned E2E DAP process {candidate.Id}.");
            candidate.Kill(entireProcessTree:true);
            candidate.WaitForExit(5000);
        }
        catch(InvalidOperationException) { }
        catch(System.ComponentModel.Win32Exception) { }
    }
}

// Build is deliberately outside the measured DAP startup path. Reuse the
// stable E2E output only when no DAP source/project input is newer than the
// executable. This keeps repeated runs fast without ever testing stale product
// code after a source change.
var dapSourceRoot=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dapAppProject)!,".."));
var dapExecutableTimestamp=File.Exists(dapExecutable)
    ? File.GetLastWriteTimeUtc(dapExecutable)
    : DateTime.MinValue;
var dapBuildInputExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".cs", ".csproj", ".props", ".targets", ".json", ".sql", ".xaml", ".resx"
};
var dapBuildRequired=!File.Exists(dapExecutable)
    || Directory.EnumerateFiles(dapSourceRoot,"*",SearchOption.AllDirectories)
        .Where(path=>dapBuildInputExtensions.Contains(Path.GetExtension(path)))
        .Any(path=>File.GetLastWriteTimeUtc(path)>dapExecutableTimestamp);

if(dapBuildRequired)
{
    StartupMark("orphan cleanup completed; DAP.App build starting");
    using(var dapBuildProcess=Process.Start(new ProcessStartInfo
    {
        FileName="dotnet",
        Arguments=$"build \"{dapAppProject}\" --nologo --verbosity quiet --output \"{dapBuildOutput}\"",
        UseShellExecute=false,
        CreateNoWindow=true,
        RedirectStandardOutput=true,
        RedirectStandardError=true
    }) ?? throw new Exception("DAP.App build process could not be started."))
    {
        var buildStdOutTask=dapBuildProcess.StandardOutput.ReadToEndAsync();
        var buildStdErrTask=dapBuildProcess.StandardError.ReadToEndAsync();
        await dapBuildProcess.WaitForExitAsync();
        if(dapBuildProcess.ExitCode!=0)
            throw new Exception(
                $"DAP.App build failed. ExitCode={dapBuildProcess.ExitCode}.{Environment.NewLine}" +
                $"STDOUT:{Environment.NewLine}{await buildStdOutTask}{Environment.NewLine}" +
                $"STDERR:{Environment.NewLine}{await buildStdErrTask}");
    }
    StartupMark("DAP.App build completed");
}
else
{
    StartupMark("DAP.App build skipped; stable E2E output is current");
}

if(!File.Exists(dapExecutable))
    throw new Exception($"Built DAP executable not found at {dapExecutable}");

using var dapProcess=new Process
{
    StartInfo=new ProcessStartInfo
    {
        FileName=dapExecutable,
        Arguments=$"--learner-web {DapTestCrmGuideSeed.GuideId} --cdp http://127.0.0.1:{dapCdpPort} --page-url-contains localhost:5200",
        UseShellExecute=false,
        CreateNoWindow=true,
        RedirectStandardOutput=true,
        RedirectStandardError=true
    }
};
dapProcess.StartInfo.Environment["DAP_DATABASE_PATH"]=dapDbPath;
var dapStartupTimer=Stopwatch.StartNew();
if(!dapProcess.Start())
    throw new Exception("DAP.exe process could not be started.");
StartupMark("DAP.exe process started");

// Ctrl+C can terminate the E2E before async finally cleanup gets a chance to
// run. Register a synchronous process-exit safety net scoped only to the DAP
// process created by this test.
void KillOwnedDapProcess()
{
    try
    {
        if(!dapProcess.HasExited)
        {
            dapProcess.Kill(entireProcessTree:true);
            dapProcess.WaitForExit(5000);
        }
    }
    catch(InvalidOperationException) { }
    catch(System.ComponentModel.Win32Exception) { }
}
AppDomain.CurrentDomain.ProcessExit+=(_,_)=>KillOwnedDapProcess();

var dapStdOutTask=dapProcess.StandardOutput.ReadToEndAsync();
var dapStdErrLines=new System.Collections.Concurrent.ConcurrentQueue<string>();
dapProcess.ErrorDataReceived+=(_,eventArgs)=>
{
    if(eventArgs.Data is not null)
        dapStdErrLines.Enqueue(eventArgs.Data);
};
dapProcess.BeginErrorReadLine();

try
{
var dapContent=await Content();
var dapBubble=dapContent.Locator("#dap-guide-bubble");
var dapStartupDeadline=DateTime.UtcNow.AddSeconds(30);
while(await dapBubble.CountAsync()==0 && DateTime.UtcNow<dapStartupDeadline)
{
    if(dapProcess.HasExited)
    {
        var dapStdOut=await dapStdOutTask;
        var dapStdErr=string.Join(Environment.NewLine,dapStdErrLines);
        throw new Exception(
            $"DAP.exe exited before presenting the first bubble. ExitCode={dapProcess.ExitCode}.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{dapStdOut}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{dapStdErr}");
    }

    await page.WaitForTimeoutAsync(100);
    dapContent=await Content();
    dapBubble=dapContent.Locator("#dap-guide-bubble");
}
if(await dapBubble.CountAsync()==0)
    throw new TimeoutException("DAP.exe did not present the first bubble within 30 seconds.");
await dapBubble.WaitForAsync(new() { Timeout = 5000 });
if(await dapBubble.TextContentAsync()!=dapStep.Bubble.Content)
    throw new Exception("DAP Web bubble content mismatch.");
dapStartupTimer.Stop();
Console.WriteLine($"DAP.exe startup to first bubble: {dapStartupTimer.Elapsed.TotalMilliseconds:F0} ms");
StartupMark("first DAP bubble observed; Scenario 1 can proceed");
var dapStartupDiagnostics=string.Join(Environment.NewLine,dapStdErrLines);
if(!string.IsNullOrWhiteSpace(dapStartupDiagnostics))
    Console.WriteLine(dapStartupDiagnostics);
Console.WriteLine("DAP production Web bubble from SQLite: PASS");


// Automatic validation belongs to DAP.exe. With guide orchestration active,
// Step 1 can be replaced by Step 2 between polling intervals; absence of any
// bubble is therefore not a valid completion signal. Require the persisted
// second Step to become the active bubble instead.
await (await Content()).Locator("[name='name']").WaitForAsync();
await Fill("[name='name']","אלפא");
var dapAdvancedToSecondStep=false;
for(var i=0;i<50;i++)
{
    dapContent=await Content();
    var activeBubble=dapContent.Locator("#dap-guide-bubble");
    if(await activeBubble.CountAsync()==1
        && await activeBubble.TextContentAsync()==dapSecondStep.Bubble.Content)
    {
        dapAdvancedToSecondStep=true;
        break;
    }
    await page.WaitForTimeoutAsync(100);
}
if(!dapAdvancedToSecondStep)
    throw new Exception("DAP Guide Runtime did not advance to the second Step after value-not-empty validation succeeded.");

var dapSecondBubble=dapContent.Locator("#dap-guide-bubble");
if(await dapSecondBubble.TextContentAsync()!=dapSecondStep.Bubble.Content)
    throw new Exception("DAP Guide Runtime second Step bubble content mismatch.");
Console.WriteLine("DAP Learner Web Runtime automatic validation completion: PASS");
Console.WriteLine("DAP Guide Runtime Step 1 -> Step 2 transition: PASS");

await Click("#customer-search button.primary");
await WaitReady();

// From here the production Guide continues across real CRM navigation. The E2E
// waits for each instruction before acting, so the same Guide can be followed
// manually without any test-only bubble behavior.
await WaitForGuideStep(3);
await Click("#search-results tbody tr.clickable:first-child");
await WaitReady();

await WaitForGuideStep(4);
await Click("tbody tr.clickable:first-child");
await WaitReady();

await WaitForGuideStep(5);
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
var frame=await Content();
var siteCasesRoute=new Uri(frame.Url).Fragment;
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
await Click("th button[data-sort='status']");
await WaitReady();

// Create a fresh open Case so repeated runs never depend on mutated seed data.
await WaitForGuideStep(6);
await Click("button.primary:has-text('פניה חדשה')");
await WaitReady();

await WaitForGuideStep(7);
await Fill("[name='subject']","תקלה בחיבור לאינטרנט");

await WaitForGuideStep(8);
await Fill("[name='description']","הלקוח מדווח על חיבור לא יציב.");

await WaitForGuideStep(9);
await SaveSuccess();

// The first complete business Guide ends here. Everything below remains the
// broader CRM resilience suite and must not depend on an active Guide bubble.
await page.WaitForTimeoutAsync(250);
if(await (await Content()).Locator("#dap-guide-bubble").CountAsync()!=0)
    throw new Exception("Completed case-creation Guide still has an active bubble.");
Console.WriteLine("DAP complete customer -> new Case Guide: PASS");
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
// Business scenario: an agent moves a customer case from Open to In Progress; the server
// recalculates the form and DAP continues on the same logical case.
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
frame=await Content();
if(await frame.Locator("[name='subject']").InputValueAsync()!="תקלה בחיבור לאינטרנט")
    throw new Exception("Unsaved Subject was not preserved after server validation refresh.");
if(await frame.Locator("[name='description']").InputValueAsync()!="הלקוח מדווח על חיבור לא יציב.")
    throw new Exception("Unsaved Description was not preserved after server validation refresh.");
await Select("[name='closeReason']","טופל");
await SaveSuccess();

// 4. Content-document reload while remaining on the persisted Case.
// Business scenario: the active CRM document is rebuilt, and DAP must reacquire
// the replacement frame without losing the current business record.
var beforeReloadRoute=frame.Url;
if(!beforeReloadRoute.Contains($"#/case/{createdCaseId}",StringComparison.Ordinal))
    throw new Exception("Expected to remain on the created Case before Content reload.");
await frame.EvaluateAsync("() => location.reload()");
await page.Locator("#content-frame").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10000 });
frame=await Content();
await frame.Locator("h1:has-text('פניה')").WaitForAsync();
if(!frame.Url.Contains($"#/case/{createdCaseId}",StringComparison.Ordinal))
    throw new Exception("Content reload did not preserve the active Case route.");
if(await frame.Locator("[name='status']").InputValueAsync()!="סגורה")
    throw new Exception("Content reload did not preserve the saved Case status.");
if(await frame.Locator("[name='subject']").InputValueAsync()!="תקלה בחיבור לאינטרנט")
    throw new Exception("Content reload did not preserve the saved Case subject.");
if(await frame.Locator("[name='closeReason']").InputValueAsync()!="טופל")
    throw new Exception("Content reload did not preserve the saved Close Reason.");

// 7. CRM tab switching: leave the Case, switch between Site tabs, and return to Cases.
// Business scenario: an agent checks Leads and then returns to the Cases workspace
// without losing the current Site context or accidentally leaving the customer.
//
// Use the same user-facing Site breadcrumb navigation as the application.
// Click() handles the real Content iframe replacement lifecycle; the test does
// not call internal TestCRM navigation APIs or bypass the UI.
frame=await Content();
await Click(".breadcrumb a[data-go^='#/site/']");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
if(await frame.Locator($"button.grid-open[data-go='#/case/{createdCaseId}']").CountAsync()==0)
    throw new Exception("Created Case was not preserved after Site tab switching.");

// 8. Continue legitimate agent work into Leads.
// We are already back on the Site Cases tab from Scenario 5, so the next
// business action is simply to open the Leads tab.
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

// 9. Conditional Lead target disappearance/reappearance.
// Business scenario: changing the Lead status changes which dependent business field
// exists in the DOM. DAP must not keep a stale reference to the old target.
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Selected Service target did not appear after successful-close status.");

await Select("[name='status']","חדש");
frame=await Content();
if(await frame.Locator("[name='selectedService']").CountAsync()!=0)
    throw new Exception("Selected Service target did not disappear after returning Lead to New status.");

await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Selected Service target did not reappear after returning to successful-close status.");

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

// 10. Layout shift: a dependent Lead field is inserted into the form.
// Business scenario: changing a status adds a business field above the action row.
// The logical Delete target remains the same, but its screen position changes.
// DAP must resolve the target from the live DOM rather than retaining old coordinates.
frame=await Content();
// Re-enter the Site through the user-facing breadcrumb and Site list.
var customerCrumb=frame.Locator(".breadcrumb a[data-go^='#/customer/']").First;
await customerCrumb.WaitForAsync();
await MoveTo(customerCrumb);
await customerCrumb.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('אתרים')").WaitForAsync();
await Click("tbody tr.clickable:first-child");
await WaitReady();
frame=await Content();
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
await frame.Locator("tbody tr.clickable").First.WaitForAsync();
await frame.Locator("tbody tr.clickable").First.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("#delete-lead").WaitForAsync();
// First remove the dependent field so the target is measured in the compact layout.
await Select("[name='status']","חדש");
frame=await Content();
if(await frame.Locator("[name='selectedService']").CountAsync()!=0)
    throw new Exception("Dependent field did not disappear before layout-shift measurement.");
var deleteTarget=frame.Locator("#delete-lead");
var beforeBox=await deleteTarget.BoundingBoxAsync();
if(beforeBox is null) throw new Exception("Could not resolve Delete Lead target before layout shift.");
// Now trigger the existing server-driven status change that inserts the dependent field.
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
deleteTarget=frame.Locator("#delete-lead");
var afterBox=await deleteTarget.BoundingBoxAsync();
if(afterBox is null) throw new Exception("Could not re-resolve Delete Lead target after layout shift.");
if(Math.Abs(afterBox.Y-beforeBox.Y)<1)
    throw new Exception("Expected the dependent field to move the Delete Lead target, but its position did not change.");
await MoveTo(deleteTarget);
await deleteTarget.WaitForAsync();

// 9. Consecutive server updates / race resilience.
// Business scenario: an agent changes the same Lead status twice while the CRM is
// rebuilding the dependent form. DAP must not retain the first update's Frame or
// target and must settle on the final business state.
await Select("[name='status']","חדש");
frame=await Content();
var statusTarget=frame.Locator("[name='status']");
await statusTarget.SelectOptionAsync("נסגר בהצלחה");
await WaitReady();
frame=await Content();
await frame.Locator("[name='status']").WaitForAsync();
if(await frame.Locator("[name='status']").InputValueAsync()!="נסגר בהצלחה")
    throw new Exception("Consecutive status updates did not settle on the final status.");
await frame.Locator("[name='selectedService']").WaitForAsync();
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Final status did not re-render the dependent business target.");
await MoveTo(frame.Locator("[name='selectedService']"));

// 10. Business-context isolation.
// Business scenario: after working in the current Lead, the agent opens another
// Case under the same Site. DAP must resolve the new record's live target and
// never retain the previous Lead/Case DOM context.
var leadSiteCrumb=frame.Locator(".breadcrumb a[data-go^='#/site/'][data-go$='/leads']").First;
await leadSiteCrumb.WaitForAsync();
await MoveTo(leadSiteCrumb);
await leadSiteCrumb.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
var caseRows=frame.Locator("button.grid-open");
if(await caseRows.CountAsync()<1)
    throw new Exception("No Case rows available for business-context switch.");
await Click("tbody tr:first-child button.grid-open");
await WaitReady();
frame=await Content();
await frame.Locator("h1:has-text('פניה')").WaitForAsync();
var switchedCaseRoute=frame.Url;
if(!switchedCaseRoute.Contains("#/case/",StringComparison.Ordinal))
    throw new Exception("Business-context switch did not open a Case record.");
var switchedCaseStatus=frame.Locator("[name='status']");
await switchedCaseStatus.WaitForAsync();
await MoveTo(switchedCaseStatus);
if(await switchedCaseStatus.CountAsync()!=1)
    throw new Exception("Case target resolution is ambiguous after business-context switch.");

// 10b. Return to the Site through the real breadcrumb; this proves the active
// context can leave and re-enter without relying on a stale record reference.
var switchedSiteCrumb=frame.Locator(".breadcrumb a[data-go^='#/site/']").First;
await switchedSiteCrumb.WaitForAsync();
await switchedSiteCrumb.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();

// 10. Delete the Case created by this run through the real UI.
// Business-context scenario already returned us to the Site's Cases tab.
frame=await Content();
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

// 11. Cross-frame navigation: a user action in the Header frame changes the active
// Content document. This is a real user-facing interaction and intentionally does not
// call internal TestCRM navigation functions.
var headerFrame=page.Frames.FirstOrDefault(x=>x.Name=="dap-header")
    ?? throw new Exception("Header frame was not found.");
var header=headerFrame.Locator("#portal-header");
await MoveTo(header);
await header.ClickAsync();
await WaitReady();
frame=await Content();
if(!new Uri(frame.Url).Fragment.Equals("#/",StringComparison.Ordinal))
    throw new Exception("Header navigation did not return Content to the customer workspace.");
await frame.Locator("h1:has-text('חיפוש לקוח')").WaitForAsync();

Console.WriteLine("PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.");
await page.WaitForTimeoutAsync(visualMode ? 1500 : 0);
}
finally
{
    KillOwnedDapProcess();

}
