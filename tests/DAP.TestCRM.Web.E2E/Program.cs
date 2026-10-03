using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DAP.TestCRM.Web.E2E;
using Microsoft.Playwright;
using DAP.Core.Targets;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

const string baseUrl = "http://localhost:5200";

int? manualFromStep = null;
int? visualFromStep = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--manual-from-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsedManualStep) || parsedManualStep < 1)
            throw new ArgumentException("--manual-from-step requires a positive Guide Step order.");
        manualFromStep = parsedManualStep;
        continue;
    }

    if (args[i].Equals("--visual-from-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsedVisualStep) || parsedVisualStep < 1)
            throw new ArgumentException("--visual-from-step requires a positive Guide Step order.");
        visualFromStep = parsedVisualStep;
    }
}

if (manualFromStep is not null && visualFromStep is not null)
    throw new ArgumentException("--manual-from-step and --visual-from-step cannot be combined.");

if (args.Contains("--reset-guide", StringComparer.OrdinalIgnoreCase))
{
    var resetOptions = SqliteDatabaseOptions.CreateDefault();
    var resetFactory = new SqliteConnectionFactory(resetOptions);
    await new SqliteDatabaseInitializer(resetFactory).InitializeAsync();
    var resetRepository = new SqliteGuideStepRepository(resetFactory);
    await resetRepository.RenameGuideAsync(
        DapTestCrmGuideSeed.LegacyGuideId,
        DapTestCrmGuideSeed.GuideId,
        DapTestCrmGuideSeed.GuideName);
    foreach (var step in DapTestCrmGuideSeed.CreateSteps())
        await resetRepository.SaveStepAsync(DapTestCrmGuideSeed.GuideId, step);

    Console.WriteLine($"Reset Guide '{DapTestCrmGuideSeed.GuideId}' ({DapTestCrmGuideSeed.CreateSteps().Count} steps) in {resetOptions.DatabasePath}");
    return;
}

var unguided = args.Contains("--unguided", StringComparer.OrdinalIgnoreCase);
var explicitGuided = args.Contains("--guided", StringComparer.OrdinalIgnoreCase);
if (unguided && explicitGuided)
    throw new ArgumentException("--guided and --unguided cannot be combined.");
if (unguided && (manualFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--unguided cannot be combined with --manual-from-step or --visual-from-step.");

static int ReserveTcpPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

Process? ownedTestCrmProcess = null;
Process? ownedTestCrmBackendProcess = null;
{
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var testCrmProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Web", "DAP.TestCRM.Web.csproj");
    var testCrmBackendProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Server", "DAP.TestCRM.Server.csproj");
    if (!File.Exists(testCrmProject))
        throw new FileNotFoundException("TestCRM Web project was not found.", testCrmProject);
    if (!File.Exists(testCrmBackendProject))
        throw new FileNotFoundException("TestCRM Server project was not found.", testCrmBackendProject);

    var backendPsi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"run --project \"{testCrmBackendProject}\" --no-launch-profile",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    backendPsi.Environment["ASPNETCORE_URLS"] = "http://localhost:5201";
    ownedTestCrmBackendProcess = Process.Start(backendPsi)
        ?? throw new InvalidOperationException("Could not start TestCRM backend for E2E.");
    ownedTestCrmBackendProcess.OutputDataReceived += (_, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
            Console.WriteLine($"[TestCRM Backend] {e.Data}");
    };
    ownedTestCrmBackendProcess.ErrorDataReceived += (_, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
            Console.Error.WriteLine($"[TestCRM Backend ERROR] {e.Data}");
    };
    ownedTestCrmBackendProcess.BeginOutputReadLine();
    ownedTestCrmBackendProcess.BeginErrorReadLine();

    // Every Web E2E mode is self-contained: the harness owns the TestCRM
    // backend and Web host and cleans up only the processes it started.
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"run --project \"{testCrmProject}\" --no-launch-profile",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.Environment["ASPNETCORE_URLS"] = baseUrl;
    psi.Environment["TestCrmBackendUrl"] = "http://localhost:5201";
    ownedTestCrmProcess = Process.Start(psi)
        ?? throw new InvalidOperationException("Could not start TestCRM Web host for E2E.");

    var crmReadyDeadline = DateTime.UtcNow.AddSeconds(30);
    using var http = new HttpClient();
    while (DateTime.UtcNow < crmReadyDeadline)
    {
        if (ownedTestCrmProcess.HasExited)
        {
            var stdout = await ownedTestCrmProcess.StandardOutput.ReadToEndAsync();
            var stderr = await ownedTestCrmProcess.StandardError.ReadToEndAsync();
            throw new Exception(
                $"TestCRM exited before becoming ready. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                $"STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }

        try
        {
            using var response = await http.GetAsync(baseUrl);
            if ((int)response.StatusCode < 500)
                break;
        }
        catch (HttpRequestException) { }

        await Task.Delay(200);
    }

    try
    {
        using var response = await http.GetAsync(baseUrl);
        if ((int)response.StatusCode >= 500)
            throw new Exception($"TestCRM readiness returned HTTP {(int)response.StatusCode}.");
    }
    catch (HttpRequestException ex)
    {
        throw new Exception($"TestCRM did not become ready at {baseUrl} within 30 seconds.", ex);
    }

    Console.WriteLine($"Self-contained TestCRM started at {baseUrl} (Web PID {ownedTestCrmProcess.Id}, Backend PID {ownedTestCrmBackendProcess.Id}).");
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

var e2eBrowser = Environment.GetEnvironmentVariable("DAP_E2E_BROWSER")?.Trim().ToLowerInvariant() ?? "chromium";
var browserChannel = e2eBrowser switch
{
    "chrome" => "chrome",
    "edge" => "msedge",
    "chromium" => null,
    _ => throw new ArgumentException(
        $"Unsupported DAP_E2E_BROWSER '{e2eBrowser}'. Supported values: chromium, chrome, edge.")
};

await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Channel = browserChannel,
    Headless = false,
    Args = new[] { "--start-maximized", $"--remote-debugging-port={dapCdpPort}" }
});
StartupMark($"{e2eBrowser} launched");
var e2eMode = Environment.GetEnvironmentVariable("DAP_E2E_MODE")?.Trim().ToLowerInvariant() ?? "fast";
var context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport, ExtraHTTPHeaders = new Dictionary<string,string> { ["X-DAP-E2E-Mode"] = e2eMode } });
var page = await context.NewPageAsync();
page.SetDefaultTimeout(5000);
StartupMark("browser context and page created");

var visualMode = e2eMode is "visual" or "demo";
var fastMode = !visualMode;
var switchedToVisual = visualMode;

Console.WriteLine($"E2E mode: {(unguided ? $"unguided ({e2eMode})" : visualFromStep is not null ? $"fast -> visual from Step {visualFromStep}" : visualMode ? "visual" : "fast")}");
if (visualMode || visualFromStep is not null)
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
    // Guide-step timeout detects a technical transition failure. Human-paced
    // visual/demo timing is handled separately by HumanPause.
    const int attempts=50;
    for(var i=0;i<attempts;i++)
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
    var diagnosticParts = new List<string>();
    try
    {
        var iframe = page.Locator("#content-frame");
        var iframeCount = await iframe.CountAsync();
        diagnosticParts.Add($"#content-frame count={iframeCount}");

        if (iframeCount == 1)
        {
            var src = await iframe.GetAttributeAsync("src");
            diagnosticParts.Add($"src={src ?? "<null>"}");

            var handle = await iframe.ElementHandleAsync();
            var frame = handle is null ? null : await handle.ContentFrameAsync();
            diagnosticParts.Add($"contentFrame={(frame is null ? "null" : "present")}");

            if (frame is not null)
            {
                diagnosticParts.Add($"detached={frame.IsDetached}");
                diagnosticParts.Add($"frameUrl={frame.Url}");

                if (!frame.IsDetached)
                {
                    var html = frame.Locator("html");
                    var ready = await html.GetAttributeAsync("data-dap-ready");
                    var routeState = await html.GetAttributeAsync("data-dap-route-state");
                    var routeError = await html.GetAttributeAsync("data-dap-route-error");
                    var apiUrl = await html.GetAttributeAsync("data-dap-api");
                    var apiState = await html.GetAttributeAsync("data-dap-api-state");
                    diagnosticParts.Add($"data-dap-ready={ready ?? "<null>"}");
                    diagnosticParts.Add($"route-state={routeState ?? "<null>"}");
                    diagnosticParts.Add($"route-error={routeError ?? "<null>"}");
                    diagnosticParts.Add($"api={apiUrl ?? "<null>"}");
                    diagnosticParts.Add($"api-state={apiState ?? "<null>"}");
                }
            }
        }

        diagnosticParts.Add("pageFrames=[" + string.Join(", ", page.Frames.Select(x => $"{x.Name}:{x.Url}")) + "]");
    }
    catch (Exception ex)
    {
        diagnosticParts.Add($"diagnostic-error={ex.GetType().Name}: {ex.Message}");
    }

    throw new Exception("Stable content iframe not found. " + string.Join("; ", diagnosticParts));
}
async Task WaitReady()
{
    // Content() already proves that the current live Content frame reached the
    // TestCRM application-ready marker. Waiting for DOMContentLoaded after that
    // introduces a browser-dependent lifecycle race: Chrome can complete (or
    // replace) the document before this waiter is registered.
    //
    // Reacquire the live frame and use the application's own readiness contract
    // instead. This is also the state DAP actually cares about after a
    // PeopleSoft-style server update.
    var f = await Content();
    await f.Locator("#server-busy").WaitForAsync(new()
    {
        State = WaitForSelectorState.Hidden,
        Timeout = 5000
    });
}
async Task HumanPause(int ms=320)
{
    if (visualMode) await page.WaitForTimeoutAsync(ms);
}
double cursorX=24,cursorY=24;
async Task MoveTo(ILocator target)
{
    // Every visible learner action must operate on the exact DOM element owned
    // by the active production bubble. This turns Guide/E2E synchronization
    // into an executable invariant instead of relying on visually similar
    // selectors in two separate places.
    if(!unguided)
    {
        var matchesActiveGuideTarget=await target.EvaluateAsync<bool>(
            @"el => {
                const bubble=el.ownerDocument.getElementById('dap-guide-bubble');
                return !!bubble && bubble.__dapTarget === el;
            }");
        if(!matchesActiveGuideTarget)
            throw new Exception("Visible E2E action target does not match the active DAP Guide target.");
    }

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
        // The transient #content-frame-next can be created and promoted before
        // Playwright observes its Attached state (especially in visual mode).
        // Wait for the stable outcome instead: the active Content frame has
        // finished the replacement lifecycle and reports itself ready.
        await page.Locator("#content-frame").WaitForAsync(new() {
            State = WaitForSelectorState.Attached, Timeout = 10000
        });
        await page.Locator("#content-frame-next").WaitForAsync(new() {
            State = WaitForSelectorState.Detached, Timeout = 10000
        });
        await Content();
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

    // Finishing text entry is a distinct learner action. Move focus away so
    // the production Runtime receives the natural blur completion event; the
    // value validation is evaluated only after this point.
    await page.Keyboard.PressAsync("Tab");
    await HumanPause(120);
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
async Task WaitForSaveValidation(string field)
{
    await WaitReady();
    var f=await Content();
    await f.Locator($"[name='{field}'].validation-error").WaitForAsync();
    await f.Locator("#ps-alert button").WaitForAsync();
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

// Every Web scenario mode consumes the same persisted production Guide.
// unguided suppresses DAP.exe and bubble presentation, but the business-flow
// harness is still sequenced by the Guide in DAP.db. This keeps the Guide as
// the single source of truth without adding test-only fields to production data.
var dapDatabaseOptions=SqliteDatabaseOptions.CreateDefault();
var dapDbPath=dapDatabaseOptions.DatabasePath;
var dapFactory=new SqliteConnectionFactory(dapDatabaseOptions);
await new SqliteDatabaseInitializer(dapFactory).InitializeAsync();
var dapRepository=new SqliteGuideStepRepository(dapFactory);
await dapRepository.RenameGuideAsync(
    DapTestCrmGuideSeed.LegacyGuideId,
    DapTestCrmGuideSeed.GuideId,
    DapTestCrmGuideSeed.GuideName);
var dapSteps=await dapRepository.GetStepsAsync(DapTestCrmGuideSeed.GuideId);
Console.WriteLine($"DAP persistent guide database: {dapDbPath}");
StartupMark(unguided
    ? "persistent DAP guide loaded for unguided"
    : "persistent DAP guide loaded");

if(dapSteps.Count==0)
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' does not exist in the persistent database. " +
        "Initialize/reset the Guide explicitly before running the E2E.");
if(dapSteps.Count!=53)
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' must contain exactly 53 Steps for the canonical Web scenario; found {dapSteps.Count}.");
if(dapSteps.Select(step=>step.Order).Distinct().Count()!=dapSteps.Count
    || dapSteps.Min(step=>step.Order)!=1
    || dapSteps.Max(step=>step.Order)!=dapSteps.Count)
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' must have contiguous unique Step orders 1..{dapSteps.Count}.");
if(dapSteps.Any(step=>step.Target is null || step.Target.Runtime!=TargetRuntime.Web))
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' contains a Step without a Web target.");

if(manualFromStep is not null && !dapSteps.Any(step => step.Order == manualFromStep.Value))
    throw new ArgumentOutOfRangeException(
        nameof(manualFromStep),
        manualFromStep,
        $"Guide '{DapTestCrmGuideSeed.GuideId}' does not contain Step {manualFromStep}.");
if(visualFromStep is not null && !dapSteps.Any(step => step.Order == visualFromStep.Value))
    throw new ArgumentOutOfRangeException(
        nameof(visualFromStep),
        visualFromStep,
        $"Guide '{DapTestCrmGuideSeed.GuideId}' does not contain Step {visualFromStep}.");

var dapStdErrLines=new System.Collections.Concurrent.ConcurrentQueue<string>();
var lastScenarioGuideOrder=0;
async Task WaitForGuideStep(int order)
{
    var expected=dapSteps.Single(step=>step.Order==order);

    // The harness may wait for the same active Guide Step more than once:
    // first to synchronize a preceding transition, then again immediately
    // before performing that Step's learner action. Repeating the current Step
    // is valid; skipping forward or moving backward is not.
    if(order<lastScenarioGuideOrder || order>lastScenarioGuideOrder+1)
        throw new Exception(
            $"Canonical Web scenario requested Guide Step {order} after Step {lastScenarioGuideOrder}; expected Step {lastScenarioGuideOrder} or {lastScenarioGuideOrder+1}.");
    var advancedSequence=order==lastScenarioGuideOrder+1;
    if(advancedSequence)
        lastScenarioGuideOrder=order;

    if(unguided)
    {
        if(advancedSequence)
            Console.WriteLine($"Web unguided Guide Step {order}/{dapSteps.Count}: {expected.Id}");
        return;
    }
    for(var i=0;i<100;i++)
    {
        // A Guide may cross frame boundaries. Search live frames instead of
        // assuming every production bubble belongs to the Content iframe.
        foreach(var liveFrame in page.Frames.Where(candidate=>!candidate.IsDetached))
        {
            try
            {
                // Normal bubbles live with their target frame. A constrained
                // child frame can instead use the presentation-only top-level
                // proxy, so the harness must recognize both production surfaces.
                foreach(var selector in new[] { "#dap-guide-bubble", "#dap-guide-bubble-proxy" })
                {
                    var bubble=liveFrame.Locator(selector);
                    if(await bubble.CountAsync()==1 && await bubble.IsVisibleAsync())
                    {
                        var bubbleText=await bubble.TextContentAsync() ?? string.Empty;
                        var expectedProgress=$"שלב {order} מתוך {dapSteps.Count}";
                        if(bubbleText.Contains(expected.Bubble.Content,StringComparison.Ordinal)
                            && bubbleText.Contains(expectedProgress,StringComparison.Ordinal))
                        {
                            if(visualFromStep == order && !switchedToVisual)
                            {
                                visualMode=true;
                                fastMode=false;
                                switchedToVisual=true;
                                Console.WriteLine($"E2E mode transition: FAST -> VISUAL at Step {order}");
                            }
                            await HumanPause(500);
                            if(manualFromStep == order)
                            {
                                Console.WriteLine();
                                Console.WriteLine($"MANUAL HANDOFF: Step {order} is ready.");
                                Console.WriteLine("Automation is paused. Inspect and interact with the open browser now.");
                                Console.WriteLine("Press ENTER here when you are finished to close the run.");
                                Console.ReadLine();
                                await browser.CloseAsync();
                                if(ownedTestCrmProcess is { HasExited: false })
                                    ownedTestCrmProcess.Kill(entireProcessTree: true);
                                Environment.Exit(0);
                            }
                            return;
                        }
                    }
                }
            }
            catch(PlaywrightException) { }
        }
        await page.WaitForTimeoutAsync(100);
    }
    var recentDapDiagnostics=string.Join(
        Environment.NewLine,
        dapStdErrLines.Where(line =>
            line.StartsWith("[DAP guide]",StringComparison.Ordinal)
            || line.StartsWith("[DAP validation]",StringComparison.Ordinal)
            || line.StartsWith("[DAP bubble]",StringComparison.Ordinal)
            || line.StartsWith("[DAP runtime]",StringComparison.Ordinal)
            || line.StartsWith("[DAP runtime trace]",StringComparison.Ordinal)));
    throw new TimeoutException(
        $"DAP Guide did not present Step {order}: {expected.Id}.{Environment.NewLine}" +
        $"DAP diagnostics:{Environment.NewLine}{recentDapDiagnostics}");
}

Process? dapProcess=null;
Task<string>? dapStdOutTask=null;
void KillOwnedDapProcess()
{
    if(dapProcess is null) return;
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

try
{
if(!unguided)
{
var dapStep=dapSteps[0];
var dapSecondStep=dapSteps[1];
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

dapProcess=new Process
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
dapProcess.StartInfo.Environment["DAP_DATABASE_PATH"]=dapDbPath!;
var dapStartupTimer=Stopwatch.StartNew();
if(!dapProcess.Start())
    throw new Exception("DAP.exe process could not be started.");
StartupMark("DAP.exe process started");

// Ctrl+C can terminate the E2E before async finally cleanup gets a chance to
// run. Register a synchronous process-exit safety net scoped only to the DAP
// process created by this test.
AppDomain.CurrentDomain.ProcessExit+=(_,_)=>KillOwnedDapProcess();

dapStdOutTask=dapProcess.StandardOutput.ReadToEndAsync();
dapProcess.ErrorDataReceived+=(_,eventArgs)=>
{
    if(eventArgs.Data is not null)
        dapStdErrLines.Enqueue(eventArgs.Data);
};
dapProcess.BeginErrorReadLine();

var dapContent=await Content();
var dapBubble=dapContent.Locator("#dap-guide-bubble");
var dapStartupDeadline=DateTime.UtcNow.AddSeconds(30);
while(await dapBubble.CountAsync()==0 && DateTime.UtcNow<dapStartupDeadline)
{
    if(dapProcess.HasExited)
    {
        var dapStdOut=await dapStdOutTask!;
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
var dapBubbleText=await dapBubble.TextContentAsync() ?? string.Empty;
if(!dapBubbleText.Contains(dapStep.Bubble.Content,StringComparison.Ordinal))
    throw new Exception("DAP Web bubble instruction content mismatch.");
var expectedProgress=$"שלב 1 מתוך {dapSteps.Count}";
if(!dapBubbleText.Contains(expectedProgress,StringComparison.Ordinal))
    throw new Exception($"DAP Web bubble progress mismatch. Expected '{expectedProgress}'.");
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

// Regression guard: Step 1 now requires the exact customer name. A committed
// wrong value must keep Step 1 active; interaction alone is not completion.
await Fill("[name='name']","אלפא");
await page.WaitForTimeoutAsync(350);
dapContent=await Content();
var wrongValueBubble=dapContent.Locator("#dap-guide-bubble");
if(await wrongValueBubble.CountAsync()!=1
    || !(await wrongValueBubble.TextContentAsync() ?? string.Empty).Contains(dapStep.Bubble.Content,StringComparison.Ordinal))
    throw new Exception("Step 1 advanced even though its exact value validation was not satisfied.");
Console.WriteLine("DAP exact-value validation rejects a committed wrong value: PASS");

await Fill("[name='name']","אלפא פתרונות בע\"מ");
var dapAdvancedToSecondStep=false;
for(var i=0;i<50;i++)
{
    dapContent=await Content();
    var activeBubble=dapContent.Locator("#dap-guide-bubble");
    if(await activeBubble.CountAsync()==1
        && (await activeBubble.TextContentAsync() ?? string.Empty).Contains(dapSecondStep.Bubble.Content,StringComparison.Ordinal))
    {
        dapAdvancedToSecondStep=true;
        break;
    }
    await page.WaitForTimeoutAsync(100);
}
if(!dapAdvancedToSecondStep)
    throw new Exception("DAP Guide Runtime did not advance to the second Step after exact value validation succeeded.");

var dapSecondBubble=dapContent.Locator("#dap-guide-bubble");
var dapSecondBubbleText=await dapSecondBubble.TextContentAsync() ?? string.Empty;
if(!dapSecondBubbleText.Contains(dapSecondStep.Bubble.Content,StringComparison.Ordinal))
    throw new Exception("DAP Guide Runtime second Step bubble instruction content mismatch.");
var expectedSecondProgress=$"שלב 2 מתוך {dapSteps.Count}";
if(!dapSecondBubbleText.Contains(expectedSecondProgress,StringComparison.Ordinal))
    throw new Exception($"DAP Guide Runtime second Step progress mismatch. Expected '{expectedSecondProgress}'.");
Console.WriteLine("DAP Learner Web Runtime automatic validation completion: PASS");
Console.WriteLine("DAP Guide Runtime Step 1 -> Step 2 transition: PASS");

// Steps 1 and 2 were verified above through the production Runtime rather than
// through WaitForGuideStep, so record the same canonical sequence position.
lastScenarioGuideOrder=2;
}
else
{
    // unguided follows the same persisted Guide sequence without DAP.exe or
    // bubble presentation. Test input remains harness-owned.
    await WaitForGuideStep(1);
    await (await Content()).Locator("[name='name']").WaitForAsync();
    await Fill("[name='name']","אלפא פתרונות בע\"מ");
    await WaitForGuideStep(2);
}

await Click("#customer-search button.primary");
await WaitReady();

// From here the production Guide continues across real CRM navigation. The E2E
// waits for each instruction before acting, so the same Guide can be followed
// manually without any test-only bubble behavior.
await WaitForGuideStep(3);
await Click("#search-results tbody tr.clickable:first-child");
await WaitReady();

await WaitForGuideStep(4);
await Click("tbody tr.clickable:has-text('מטה תל אביב')");
await WaitReady();

await WaitForGuideStep(5);
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
var frame=await Content();
var siteCasesRoute=new Uri(frame.Url).Fragment;
await frame.Locator("h2:has-text('פניות')").WaitForAsync();

// Sorting is a visible learner action in the demo, so it has its own Guide Step.
// Never perform it while the next bubble is already instructing another action.
await WaitForGuideStep(6);
await Click("th button[data-sort='status']");
await WaitReady();

// Create a fresh open Case so repeated runs never depend on mutated seed data.
await WaitForGuideStep(7);
await Click("button.primary:has-text('פניה חדשה')");
await WaitReady();

await WaitForGuideStep(8);
await Fill("[name=\'subject\']","\u05ea\u05e7\u05dc\u05d4 \u05d1\u05d7\u05d9\u05d1\u05d5\u05e8 \u05dc\u05d0\u05d9\u05e0\u05d8\u05e8\u05e0\u05d8");

await WaitForGuideStep(9);
await Fill("[name='description']","הלקוח מדווח על חיבור לא יציב.");

await WaitForGuideStep(10);
await SaveSuccess();

// The Guide deliberately continues into treatment of the Case just created.
frame=await Content();
var createdCaseUrl=frame.Url;
var caseMarker="#/case/";
var casePos=createdCaseUrl.IndexOf(caseMarker,StringComparison.Ordinal);
if(casePos<0) throw new Exception("Created Case id missing from route: "+createdCaseUrl);
var createdCaseId=createdCaseUrl[(casePos+caseMarker.Length)..].Split('?', '/', '#')[0];

// Return to the Cases grid, then open exactly the Case created by this run.
// This also verifies repeated identical Open targets without relying on unique status text.
frame=await Content();
await WaitForGuideStep(11);
var casesCrumb=frame.Locator(".breadcrumb a").Nth(2);
await MoveTo(casesCrumb); await casesCrumb.ClickAsync(); await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();

await WaitForGuideStep(12);
// The visible action uses exactly the same semantic business target as the
// Guide bubble. Verify that semantic target resolves to the Case created by
// this run before clicking it.
var createdCaseTarget=frame.Locator($"button.grid-open[data-go='#/case/{createdCaseId}']");
if(await createdCaseTarget.CountAsync()!=1)
    throw new Exception("The Case created by this run is not uniquely available in the Cases grid.");
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");
await WaitReady();

// 3. Case FieldChange: disabled -> enabled and DOM reconstruction.
// Business scenario: an agent moves a customer case from Open to In Progress; the server
// recalculates the form and DAP continues on the same logical case.
// Reacquire only after the promoted replacement frame reports app-level readiness.
frame=await Content();
var notes=frame.Locator("[name='resolutionNotes']");
if(!await notes.IsDisabledAsync()) throw new Exception("Treatment Notes should start disabled for an open case.");
await WaitForGuideStep(13);
await Select("[name='status']","בטיפול");
frame=await Content(); notes=frame.Locator("[name='resolutionNotes']");
if(await notes.IsDisabledAsync()) throw new Exception("Treatment Notes did not become enabled.");

await WaitForGuideStep(14);
await Fill("[name='resolutionNotes']","בוצעה בדיקת שירות מול הלקוח והתקלה טופלה.");

Console.WriteLine(unguided ? "CRM customer -> Case treatment segment: PASS" : "DAP complete customer -> Case treatment Guide segment: PASS");

// 4. Real off-screen target / scrolling through activity history.
// Step 15 is deliberately off-screen: the production presenter keeps its bubble
// hidden until the target enters the viewport, while the learner scrolls to it.
frame=await Content();
var more=frame.Locator("#activity-more");
await HumanScrollTo(more);
await WaitForGuideStep(15);
await MoveTo(more);
await more.ClickAsync();

// 5. Continue the guided business flow into Case closure.
await WaitForGuideStep(16);
await Select("[name='status']","סגורה");

// The status change performs a real server-backed Content replacement. Wait
// for the production Guide to observe the completed learner interaction and
// present Step 17 before the harness performs any additional assertions.
await WaitForGuideStep(17);
frame=await Content();

// Case status FieldChange intentionally clears Subject. The Guide explicitly
// instructs the learner to restore it before closure.
await Fill("[name='subject']","תקלה בחיבור לאינטרנט");
frame=await Content();
await frame.Locator("[name='closeReason']").WaitForAsync();

// The rejected save is an intentional learner action in this scenario.
// Guide it explicitly, then guide dismissal of the resulting validation alert.
await WaitForGuideStep(18);
await Click("button.primary:has-text('שמור')");
await WaitForSaveValidation("closeReason");

await WaitForGuideStep(19);
await Click("#ps-alert button");
await HumanPause();

frame=await Content();
if(await frame.Locator("[name='subject']").InputValueAsync()!="תקלה בחיבור לאינטרנט")
    throw new Exception("Unsaved Subject was not preserved after server validation refresh.");
if(await frame.Locator("[name='description']").InputValueAsync()!="הלקוח מדווח על חיבור לא יציב.")
    throw new Exception("Unsaved Description was not preserved after server validation refresh.");

await WaitForGuideStep(20);
await Select("[name='closeReason']","טופל");
await WaitForGuideStep(21);
Console.WriteLine(unguided ? "CRM Case closure through validation alert and Close Reason: PASS" : "DAP guided Case closure through validation alert and Close Reason: PASS");
await SaveSuccess();

// Step 21's Save click is the learner action that advances the production
// Guide. Do not start a technical E2E document reload until DAP has completed
// that transition and presented Step 22. Otherwise the harness can destroy the
// document while the Runtime is still reconciling the validating click.
await WaitForGuideStep(22);

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
await WaitForGuideStep(22);
await Click(".breadcrumb a[data-go^='#/site/']");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
await WaitForGuideStep(23);
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
await WaitForGuideStep(24);
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
if(await frame.Locator($"button.grid-open[data-go='#/case/{createdCaseId}']").CountAsync()==0)
    throw new Exception("Created Case was not preserved after Site tab switching.");

// 8. Continue legitimate agent work into Leads.
// We are already back on the Site Cases tab from Scenario 5, so the next
// business action is simply to open the Leads tab.
await WaitForGuideStep(25);
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();

// Create a Lead in this run. After the first save the same workflow changes
// from "new" to a persisted Lead and the Delete button is rendered dynamically.
await WaitForGuideStep(26);
await Click("button.primary:has-text('ליד חדש')");
await WaitReady();
await WaitForGuideStep(27);
await Fill("[name='contactName']","לקוח בדיקת מערכת");
await WaitForGuideStep(28);
await SaveSuccess();
frame=await Content();
var dynamicDeleteLead=frame.Locator("#delete-lead");
await dynamicDeleteLead.WaitForAsync();

// 9. Conditional Lead target disappearance/reappearance.
// Business scenario: changing the Lead status changes which dependent business field
// exists in the DOM. DAP must not keep a stale reference to the old target.
await WaitForGuideStep(29);
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Selected Service target did not appear after successful-close status.");

await WaitForGuideStep(30);
await Select("[name='status']","חדש");
frame=await Content();
if(await frame.Locator("[name='selectedService']").CountAsync()!=0)
    throw new Exception("Selected Service target did not disappear after returning Lead to New status.");

await WaitForGuideStep(31);
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Selected Service target did not reappear after returning to successful-close status.");

await WaitForGuideStep(32);
await Click("button.primary:has-text('שמור')");
await WaitForSaveValidation("selectedService");
await WaitForGuideStep(33);
await Click("#ps-alert button");
await HumanPause();
await WaitForGuideStep(34);
await Select("[name='selectedService']","תמיכה מורחבת");
await WaitForGuideStep(35);
await SaveSuccess();

// Exercise the dynamically rendered Delete target in the same Lead context:
// no navigation away and no reopening of the record.
frame=await Content();
dynamicDeleteLead=frame.Locator("#delete-lead");
await WaitForGuideStep(36);
await MoveTo(dynamicDeleteLead);
await dynamicDeleteLead.ClickAsync();
var confirmDeleteLead=frame.Locator("#ps-confirm [data-answer='yes']");
await confirmDeleteLead.WaitForAsync();
await WaitForGuideStep(37);
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
await WaitForGuideStep(38);
await MoveTo(customerCrumb);
await customerCrumb.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('אתרים')").WaitForAsync();
await WaitForGuideStep(39);
await Click("tbody tr.clickable:has-text('מטה תל אביב')");
await WaitReady();
frame=await Content();
await WaitForGuideStep(40);
await Click("nav.tabs button:has-text('לידים')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
await frame.Locator("tbody tr.clickable").First.WaitForAsync();
await WaitForGuideStep(41);
await Click("tbody tr.clickable:has-text('אבי כהן')");
await WaitReady();
frame=await Content();
await frame.Locator("#delete-lead").WaitForAsync();
// First remove the dependent field so the target is measured in the compact layout.
await WaitForGuideStep(42);
await Select("[name='status']","חדש");
frame=await Content();
if(await frame.Locator("[name='selectedService']").CountAsync()!=0)
    throw new Exception("Dependent field did not disappear before layout-shift measurement.");
var deleteTarget=frame.Locator("#delete-lead");
var beforeBox=await deleteTarget.BoundingBoxAsync();
if(beforeBox is null) throw new Exception("Could not resolve Delete Lead target before layout shift.");
// Now trigger the existing server-driven status change that inserts the dependent field.
await WaitForGuideStep(43);
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='selectedService']").WaitForAsync();
deleteTarget=frame.Locator("#delete-lead");
var afterBox=await deleteTarget.BoundingBoxAsync();
if(afterBox is null) throw new Exception("Could not re-resolve Delete Lead target after layout shift.");
if(Math.Abs(afterBox.Y-beforeBox.Y)<1)
    throw new Exception("Expected the dependent field to move the Delete Lead target, but its position did not change.");
await deleteTarget.WaitForAsync();

// 9. Consecutive server updates / race resilience.
// Business scenario: an agent changes the same Lead status twice while the CRM is
// rebuilding the dependent form. DAP must not retain the first update's Frame or
// target and must settle on the final business state.
await WaitForGuideStep(44);
await Select("[name='status']","חדש");
frame=await Content();
await WaitForGuideStep(45);
await Select("[name='status']","נסגר בהצלחה");
frame=await Content();
await frame.Locator("[name='status']").WaitForAsync();
if(await frame.Locator("[name='status']").InputValueAsync()!="נסגר בהצלחה")
    throw new Exception("Consecutive status updates did not settle on the final status.");
await frame.Locator("[name='selectedService']").WaitForAsync();
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Final status did not re-render the dependent business target.");
if(await frame.Locator("[name='selectedService']").CountAsync()!=1)
    throw new Exception("Final dependent business target is not uniquely resolved.");

// 10. Business-context isolation.
// Business scenario: after working in the current Lead, the agent reopens the
// Case created by this run under the same Site. DAP must resolve that persisted
// business identity from the live grid and never retain the previous Lead context.
var leadSiteCrumb=frame.Locator(".breadcrumb a[data-go^='#/site/'][data-go$='/leads']").First;
await leadSiteCrumb.WaitForAsync();
await WaitForGuideStep(46);
await MoveTo(leadSiteCrumb);
await leadSiteCrumb.ClickAsync();
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('לידים')").WaitForAsync();
await WaitForGuideStep(47);
await Click("nav.tabs button:has-text('פניות')");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();
var caseRows=frame.Locator("button.grid-open");
if(await caseRows.CountAsync()<1)
    throw new Exception("No Case rows available for business-context switch.");
await WaitForGuideStep(48);
var contextCreatedCaseTarget=frame.Locator($"button.grid-open[data-go='#/case/{createdCaseId}']");
if(await contextCreatedCaseTarget.CountAsync()!=1)
    throw new Exception("The Case created by this run is not uniquely available for the context reopen.");
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");
await WaitReady();
frame=await Content();
await frame.Locator("h1:has-text('פניה')").WaitForAsync();
var switchedCaseRoute=frame.Url;
if(!switchedCaseRoute.Contains("#/case/",StringComparison.Ordinal))
    throw new Exception("Business-context switch did not open a Case record.");
var switchedCaseStatus=frame.Locator("[name='status']");
await switchedCaseStatus.WaitForAsync();
if(await switchedCaseStatus.CountAsync()!=1)
    throw new Exception("Case target resolution is ambiguous after business-context switch.");

// 10b. Return to the Site through the real breadcrumb; this proves the active
// context can leave and re-enter without relying on a stale record reference.
var switchedSiteCrumb=frame.Locator(".breadcrumb a[data-go^='#/site/']").First;
await switchedSiteCrumb.WaitForAsync();
await WaitForGuideStep(49);
await Click(".breadcrumb a[data-go^='#/site/']");
await WaitReady();
frame=await Content();
await frame.Locator("h2:has-text('פניות')").WaitForAsync();

// 10. Delete the Case created by this run through the real UI.
// Business-context scenario already returned us to the Site's Cases tab.
frame=await Content();
await WaitForGuideStep(50);
var finalCreatedCaseTarget=frame.Locator($"button.grid-open[data-go='#/case/{createdCaseId}']");
if(await finalCreatedCaseTarget.CountAsync()!=1)
    throw new Exception("The Case created by this run is not uniquely available for final reopen.");
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");
await WaitReady();
await WaitForGuideStep(51);
await Click("#delete-case");
frame=await Content();
var confirmDelete=frame.Locator("#ps-confirm [data-answer='yes']");
await confirmDelete.WaitForAsync();
await WaitForGuideStep(52);
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
await WaitForGuideStep(53);
// Keep the final Guide bubble visible long enough to be observed in visual mode
// before the E2E performs the action that completes the Guide.
if(visualMode)
    await page.WaitForTimeoutAsync(1200);
await MoveTo(header);
await header.ClickAsync();
await WaitReady();
frame=await Content();
if(!new Uri(frame.Url).Fragment.Equals("#/",StringComparison.Ordinal))
    throw new Exception("Header navigation did not return Content to the customer workspace.");
await frame.Locator("h1:has-text('חיפוש לקוח')").WaitForAsync();

if(lastScenarioGuideOrder!=dapSteps.Count)
    throw new Exception(
        $"Canonical Web scenario completed after Guide Step {lastScenarioGuideOrder}; expected {dapSteps.Count}.");

Console.WriteLine(unguided
    ? "PASS: Web unguided executed the canonical 53-step scenario sequenced by the persisted Guide in DAP.db, without DAP.exe or bubbles."
    : "PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.");
await page.WaitForTimeoutAsync(visualMode ? 1500 : 0);
}
finally
{
    KillOwnedDapProcess();

    if (ownedTestCrmProcess is not null)
    {
        try
        {
            if (!ownedTestCrmProcess.HasExited)
            {
                ownedTestCrmProcess.Kill(entireProcessTree: true);
                ownedTestCrmProcess.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally
        {
            ownedTestCrmProcess.Dispose();
        }
    }

    if (ownedTestCrmBackendProcess is not null)
    {
        try
        {
            if (!ownedTestCrmBackendProcess.HasExited)
            {
                ownedTestCrmBackendProcess.Kill(entireProcessTree: true);
                ownedTestCrmBackendProcess.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally
        {
            ownedTestCrmBackendProcess.Dispose();
        }
    }
}
