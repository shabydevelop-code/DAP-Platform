using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using DAP.TestCRM.Web.E2E;
using DAP.Core.Targets;
using DAP.Core.Guides;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

const string baseUrl = "http://localhost:5200";

int? manualFromStep = null;
int? visualFromStep = null;
string? publishedDapDirectory = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--published-dap", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            throw new ArgumentException("--published-dap requires a directory containing DAP.exe.");
        publishedDapDirectory = Path.GetFullPath(args[++i]);
        continue;
    }

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
    var resetSteps = DapTestCrmGuideSeed.CreateSteps();
    await resetRepository.ReplaceStepsAsync(DapTestCrmGuideSeed.GuideId, resetSteps);

    Console.WriteLine($"Reset Guide '{DapTestCrmGuideSeed.GuideId}' ({resetSteps.Count} steps) in {resetOptions.DatabasePath}");
    return;
}

var unguided = args.Contains("--unguided", StringComparer.OrdinalIgnoreCase);
var explicitGuided = args.Contains("--guided", StringComparer.OrdinalIgnoreCase);
var manual = args.Contains("--manual", StringComparer.OrdinalIgnoreCase);
if (unguided && explicitGuided)
    throw new ArgumentException("--guided and --unguided cannot be combined.");
if (manual && (unguided || explicitGuided || manualFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--manual cannot be combined with --guided, --unguided, --manual-from-step, or --visual-from-step.");
if (unguided && (manualFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--unguided cannot be combined with --manual-from-step or --visual-from-step.");

static void EnsurePortFree(int port)
{
    TcpListener? listener = null;
    try
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
    }
    catch (SocketException ex)
    {
        throw new InvalidOperationException(
            $"Port {port} is already in use. Stop the existing process that owns this TestCRM port before starting a new Web E2E/manual run.",
            ex);
    }
    finally
    {
        listener?.Stop();
    }
}

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var testCrmProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Web", "DAP.TestCRM.Web.csproj");
var testCrmBackendProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Server", "DAP.TestCRM.Server.csproj");
var dapAppProject = Path.Combine(repoRoot, "src", "DAP.App", "DAP.App.csproj");
var nativeHostProject = Path.Combine(repoRoot, "src", "DAP.Runtime.Web.NativeHost", "DAP.Runtime.Web.NativeHost.csproj");
var diagnosticsRoot = Environment.GetEnvironmentVariable("DAP_DIAGNOSTICS_ROOT");
if (!string.IsNullOrWhiteSpace(diagnosticsRoot))
    diagnosticsRoot = Path.GetFullPath(diagnosticsRoot!);

// DAP_DIAGNOSTICS_ROOT can survive in a developer PowerShell session after a
// packaged diagnostic run. Never let that stale environment value silently turn
// a repo E2E run into a mixed repo/package run. Packaged mode is valid only when
// this runner itself is executing from that Diagnostics package.
var packagedDiagnostics = diagnosticsRoot is not null
    && Path.GetFullPath(AppContext.BaseDirectory).StartsWith(
        Path.Combine(diagnosticsRoot, "Runners") + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);
var packagedServerDirectory = packagedDiagnostics ? Path.Combine(diagnosticsRoot!, "TestCRM", "Server") : null;
var packagedWebDirectory = packagedDiagnostics ? Path.Combine(diagnosticsRoot!, "TestCRM", "Web") : null;
var packagedDapDirectory = packagedDiagnostics ? Path.GetFullPath(Path.Combine(diagnosticsRoot!, "..")) : null;
var webRunRoot = Path.Combine(
    Path.GetTempPath(),
    "DAP",
    "E2E",
    "Web",
    Guid.NewGuid().ToString("N"));
var backendOutput = Path.Combine(webRunRoot, "Server");
var webOutput = Path.Combine(webRunRoot, "Web");
var dapOutput = Path.Combine(webRunRoot, "DAP");

async Task BuildNativeHostAsync()
{
    Console.WriteLine("Refreshing DAP Native Host for extension-native E2E transport.");

    // The registered Native Host loads directly from this project's normal bin
    // directory, so any running host locks the DLLs MSBuild must replace.
    // Terminate stale/current host instances before building, not after.
    foreach (var process in Process.GetProcessesByName("DAP.Runtime.Web.NativeHost"))
    {
        try
        {
            if (!process.HasExited)
            {
                Console.WriteLine($"Stopping DAP Native Host PID {process.Id} before rebuild.");
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                    throw new InvalidOperationException(
                        $"DAP Native Host PID {process.Id} did not exit within 5 seconds.");
            }
        }
        catch (InvalidOperationException) when (process.HasExited) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally
        {
            process.Dispose();
        }
    }

    using var build = Process.Start(new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build \"{nativeHostProject}\" --nologo --verbosity minimal",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }) ?? throw new InvalidOperationException("Could not start DAP Native Host build.");

    var stdout = build.StandardOutput.ReadToEndAsync();
    var stderr = build.StandardError.ReadToEndAsync();
    await build.WaitForExitAsync();

    if (build.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"DAP Native Host build failed with exit code {build.ExitCode}.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{await stdout}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{await stderr}");
    }

    Console.WriteLine("DAP Native Host rebuilt; the extension will reconnect with the current binary.");
}

async Task BuildIsolatedAsync(string project, string output, string name)
{
    Directory.CreateDirectory(output);
    Console.WriteLine($"Building {name} into isolated E2E output: {output}");

    using var build = Process.Start(new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build \"{project}\" --nologo --verbosity minimal --output \"{output}\"",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }) ?? throw new InvalidOperationException($"Could not start isolated build for {name}.");

    var stdout = build.StandardOutput.ReadToEndAsync();
    var stderr = build.StandardError.ReadToEndAsync();
    await build.WaitForExitAsync();

    if (build.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"{name} isolated build failed with exit code {build.ExitCode}.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{await stdout}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{await stderr}");
    }
}

void TryKillOwnedProcessTree(Process? process)
{
    if (process is null)
        return;

    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000) && !process.HasExited)
            {
                // A successful Kill request is asynchronous. Retry once so an
                // E2E-owned DAP cannot survive the runner and keep a published
                // package locked after Ctrl+C/process-exit cleanup.
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }
    catch (InvalidOperationException) { }
    catch (System.ComponentModel.Win32Exception) { }
}

Process? ownedTestCrmProcess = null;
Process? ownedTestCrmBackendProcess = null;
Process? dapProcess = null;
Task<string>? dapStdOutTask = null;
var testCrmWebStdOut = new System.Collections.Concurrent.ConcurrentQueue<string>();
var testCrmWebStdErr = new System.Collections.Concurrent.ConcurrentQueue<string>();

void KillOwnedDapProcess()
{
    TryKillOwnedProcessTree(dapProcess);
}

void KillOwnedWebChildren()
{
    KillOwnedDapProcess();
    TryKillOwnedProcessTree(ownedTestCrmProcess);
    TryKillOwnedProcessTree(ownedTestCrmBackendProcess);
}

EventHandler webProcessExitCleanup = (_, _) => KillOwnedWebChildren();
ConsoleCancelEventHandler webCancelCleanup = (_, _) => KillOwnedWebChildren();
AppDomain.CurrentDomain.ProcessExit += webProcessExitCleanup;
Console.CancelKeyPress += webCancelCleanup;

{
    if (!packagedDiagnostics && !File.Exists(testCrmProject))
        throw new FileNotFoundException("TestCRM Web project was not found.", testCrmProject);
    if (!packagedDiagnostics && !File.Exists(testCrmBackendProject))
        throw new FileNotFoundException("TestCRM Server project was not found.", testCrmBackendProject);
    if (!packagedDiagnostics && publishedDapDirectory is null && !File.Exists(dapAppProject))
        throw new FileNotFoundException("DAP.App project was not found.", dapAppProject);
    if (!packagedDiagnostics && !File.Exists(nativeHostProject))
        throw new FileNotFoundException("DAP Native Host project was not found.", nativeHostProject);
    if (publishedDapDirectory is not null && !File.Exists(Path.Combine(publishedDapDirectory, "DAP.exe")))
        throw new FileNotFoundException("Published DAP.exe was not found.", Path.Combine(publishedDapDirectory, "DAP.exe"));

    // Fail before building/launching anything if a prior or unrelated process
    // already owns the canonical TestCRM ports. The runner never kills an
    // arbitrary port owner.
    EnsurePortFree(5200);
    EnsurePortFree(5201);

    if (!packagedDiagnostics)
    {
        // The Chrome/Edge registration points to the Native Host project's
        // normal bin output. Build that exact binary and terminate stale host
        // processes before opening the browser so the extension must reconnect
        // through the current extension-native transport.
        await BuildNativeHostAsync();

        await BuildIsolatedAsync(testCrmBackendProject, backendOutput, "TestCRM Server");
        await BuildIsolatedAsync(testCrmProject, webOutput, "TestCRM Web");

        // dotnet build --output does not carry the Web project's static content
        // into this custom isolated output. The E2E host must be a faithful
        // runnable copy, so mirror the source wwwroot into the owned run folder.
        var sourceWebRoot = Path.Combine(Path.GetDirectoryName(testCrmProject)!, "wwwroot");
        var isolatedWebRoot = Path.Combine(webOutput, "wwwroot");
        if (!Directory.Exists(sourceWebRoot))
            throw new DirectoryNotFoundException($"TestCRM Web root was not found: {sourceWebRoot}");
        Directory.CreateDirectory(isolatedWebRoot);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceWebRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceWebRoot, sourceFile);
            var destinationFile = Path.Combine(isolatedWebRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        if (!unguided && publishedDapDirectory is null)
            await BuildIsolatedAsync(dapAppProject, dapOutput, "DAP");
    }

    var effectiveBackendDirectory = packagedDiagnostics ? packagedServerDirectory! : backendOutput;
    var effectiveWebDirectory = packagedDiagnostics ? packagedWebDirectory! : webOutput;
    var backendDll = Path.Combine(effectiveBackendDirectory, "DAP.TestCRM.Server.dll");
    var webDll = Path.Combine(effectiveWebDirectory, "DAP.TestCRM.Web.dll");

    var backendPsi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"\"{backendDll}\"",
        WorkingDirectory = effectiveBackendDirectory,
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
        Arguments = $"\"{webDll}\"",
        WorkingDirectory = effectiveWebDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.Environment["ASPNETCORE_URLS"] = baseUrl;
    psi.Environment["TestCrmBackendUrl"] = "http://localhost:5201";
    ownedTestCrmProcess = Process.Start(psi)
        ?? throw new InvalidOperationException("Could not start TestCRM Web host for E2E.");

    ownedTestCrmProcess.OutputDataReceived += (_, e) =>
    {
        if (string.IsNullOrWhiteSpace(e.Data))
            return;
        testCrmWebStdOut.Enqueue(e.Data);
        while (testCrmWebStdOut.Count > 200)
            testCrmWebStdOut.TryDequeue(out string? _);
        Console.WriteLine($"[TestCRM Web] {e.Data}");
    };
    ownedTestCrmProcess.ErrorDataReceived += (_, e) =>
    {
        if (string.IsNullOrWhiteSpace(e.Data))
            return;
        testCrmWebStdErr.Enqueue(e.Data);
        while (testCrmWebStdErr.Count > 200)
            testCrmWebStdErr.TryDequeue(out string? _);
        Console.Error.WriteLine($"[TestCRM Web ERROR] {e.Data}");
    };
    ownedTestCrmProcess.BeginOutputReadLine();
    ownedTestCrmProcess.BeginErrorReadLine();

    var crmReadyDeadline = DateTime.UtcNow.AddSeconds(30);
    var crmReady = false;
    using var http = new HttpClient();

    while (DateTime.UtcNow < crmReadyDeadline)
    {
        if (ownedTestCrmProcess.HasExited)
        {
            throw new Exception(
                $"TestCRM exited before becoming ready. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                $"STDOUT tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdOut)}{Environment.NewLine}" +
                $"STDERR tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdErr)}");
        }

        try
        {
            using var response = await http.GetAsync(baseUrl);

            // HTTP success is accepted only while the exact Web-host process
            // launched by this runner is still alive. This prevents a stale
            // process on port 5200 from satisfying readiness for a failed launch.
            if ((int)response.StatusCode < 500 && !ownedTestCrmProcess.HasExited)
            {
                crmReady = true;
                break;
            }
        }
        catch (HttpRequestException)
        {
        }

        await Task.Delay(200);
    }

    if (!crmReady)
    {
        if (ownedTestCrmProcess.HasExited)
        {
            throw new Exception(
                $"TestCRM exited before becoming ready. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                $"STDOUT tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdOut)}{Environment.NewLine}" +
                $"STDERR tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdErr)}");
        }

        throw new Exception($"TestCRM did not become ready at {baseUrl} within 30 seconds.");
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

// DAP_E2E_MODE belongs only to a full --guided run. All other public
// switches have absolute semantics and must not inherit a stale PowerShell
// environment value from an earlier run.
var e2eMode = "fast";
if (explicitGuided && !manual && !unguided && manualFromStep is null && visualFromStep is null)
{
    e2eMode = Environment.GetEnvironmentVariable("DAP_E2E_MODE")?.Trim().ToLowerInvariant() ?? "fast";
    if (e2eMode is not ("fast" or "visual"))
        throw new ArgumentException(
            $"Unsupported DAP_E2E_MODE '{e2eMode}'. Supported values: fast, visual.");
}

await using var browser = await BrowserHarness.LaunchAsync(baseUrl);
StartupMark("browser launched through the installed DAP extension profile");

var page = browser.Page;
page.SetDefaultTimeout(5000);
var ownedWebTargetClosed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
void OnOwnedBrowserDisconnected(object? _, EventArgs __) => ownedWebTargetClosed.TrySetResult("browser-disconnected");
browser.Disconnected += OnOwnedBrowserDisconnected;

StartupMark("browser page connected");

var visualMode = e2eMode == "visual";
var fastMode = !visualMode;
var switchedToVisual = visualMode;

// Full Guided runs require every synthetic learner action to match the active
// production DAP target. Focused From-Step runs intentionally begin without
// DAP, so that invariant is enabled only when DAP starts at the requested Step.
var requireActiveGuideTarget =
    !unguided && manualFromStep is null && visualFromStep is null;

Console.WriteLine($"E2E mode: {(manual ? "manual" : unguided ? "unguided" : manualFromStep is not null ? $"unguided -> manual from Step {manualFromStep}" : visualFromStep is not null ? $"unguided -> visual from Step {visualFromStep}" : visualMode ? "visual" : "fast")}");
await page.AddInitScriptAsync("localStorage.setItem('dap-e2e-mode', '" + e2eMode + "'); document.documentElement.dataset.dapE2eMode = '" + e2eMode + "';");

async Task<BrowserFrame> Content()
{
    const int attempts=50;
    for(var i=0;i<attempts;i++)
    {
        try
        {
            var frame=await page.FindFrameByNameAsync("dap-content");
            if(frame is not null)
            {
                await frame.RefreshUrlAsync();
                if(await frame.Locator("html[data-dap-ready='1']").CountAsync()>0)
                    return frame;
            }
        }
        catch(BrowserHarnessException) { }
        await page.WaitForTimeoutAsync(100);
    }

    var diagnosticParts = new List<string>();
    try
    {
        var iframe = page.Locator("#content-frame");
        diagnosticParts.Add($"#content-frame count={await iframe.CountAsync()}");
        diagnosticParts.Add($"src={await iframe.GetAttributeAsync("src") ?? "<null>"}");

        var frame = await page.FindFrameByNameAsync("dap-content");
        diagnosticParts.Add($"contentFrame={(frame is null ? "null" : "present")}");
        if(frame is not null)
        {
            await frame.RefreshUrlAsync();
            diagnosticParts.Add($"frameUrl={frame.Url}");
            var html=frame.Locator("html");
            diagnosticParts.Add($"data-dap-ready={await html.GetAttributeAsync("data-dap-ready") ?? "<null>"}");
            diagnosticParts.Add($"route-state={await html.GetAttributeAsync("data-dap-route-state") ?? "<null>"}");
            diagnosticParts.Add($"route-error={await html.GetAttributeAsync("data-dap-route-error") ?? "<null>"}");
            diagnosticParts.Add($"api={await html.GetAttributeAsync("data-dap-api") ?? "<null>"}");
            diagnosticParts.Add($"api-state={await html.GetAttributeAsync("data-dap-api-state") ?? "<null>"}");
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
        State = BrowserWaitState.Hidden,
        Timeout = 5000
    });
}
async Task HumanPause(int ms=320)
{
    if (visualMode) await page.WaitForTimeoutAsync(ms);
}
async Task MoveTo(BrowserLocator target, bool enforceActiveGuideTarget = true)
{
    // Application learner actions must operate on the exact DOM element owned
    // by the active production bubble. DAP-owned overlay actions (centered
    // information confirmation and Guide completion) are intentionally not
    // target-attached, so callers can disable this invariant explicitly.
    if(enforceActiveGuideTarget && requireActiveGuideTarget)
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

    // The browser harness reports the target in browser viewport coordinates. Convert
    // that position to Windows screen coordinates so Visual mode moves the
    // real operating-system cursor instead of drawing a synthetic DOM cursor.
    var metrics=await page.EvaluateAsync<BrowserWindowMetrics>(
        @"() => ({
            ScreenX: window.screenX,
            ScreenY: window.screenY,
            OuterWidth: window.outerWidth,
            OuterHeight: window.outerHeight,
            InnerWidth: window.innerWidth,
            InnerHeight: window.innerHeight
        })");

    var sideInset=Math.Max(0,(metrics.OuterWidth-metrics.InnerWidth)/2d);
    var topInset=Math.Max(0,metrics.OuterHeight-metrics.InnerHeight-sideInset);
    var targetScreenX=(int)Math.Round(metrics.ScreenX+sideInset+box.X+localX);
    var targetScreenY=(int)Math.Round(metrics.ScreenY+topInset+box.Y+localY);

    if(!NativeCursor.GetCursorPos(out var currentCursor))
        currentCursor=new NativePoint { X=targetScreenX, Y=targetScreenY };

    const int frames=12;
    for(var frame=1;frame<=frames;frame++)
    {
        var progress=(double)frame/frames;
        var eased=1-Math.Pow(1-progress,3);
        var x=(int)Math.Round(currentCursor.X+(targetScreenX-currentCursor.X)*eased);
        var y=(int)Math.Round(currentCursor.Y+(targetScreenY-currentCursor.Y)*eased);
        if(!NativeCursor.SetCursorPos(x,y))
            throw new InvalidOperationException("Could not move the Windows cursor during Web Visual mode.");
        await page.WaitForTimeoutAsync(18);
    }

    // Keep browser hover state synchronized with the physical cursor position.
    await target.HoverAsync(new() { Position = new() { X = localX, Y = localY } });
    await HumanPause(120);
}
async Task Click(string selector)
{
    var f=await Content(); var target=f.Locator(selector);
    var replacesFrame=await target.GetAttributeAsync("data-frame-nav")=="replace";
    await MoveTo(target);
    await target.ClickAsync();

    if(replacesFrame)
    {
        // The transient #content-frame-next can be created and promoted before
        // the browser harness observes its Attached state (especially in visual mode).
        // Wait for the stable outcome instead: the active Content frame has
        // finished the replacement lifecycle and reports itself ready.
        await page.Locator("#content-frame").WaitForAsync(new() {
            State = BrowserWaitState.Attached, Timeout = 10000
        });
        await page.Locator("#content-frame-next").WaitForAsync(new() {
            State = BrowserWaitState.Detached, Timeout = 10000
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
    await page.Keyboard.TypeAsync(value);
    await HumanPause();

    // Finishing text entry is a distinct learner action. Move focus away so
    // the production Runtime receives the natural blur completion event; the
    // value validation is evaluated only after this point.
    await page.Keyboard.PressAsync("Tab");
    await HumanPause(120);
}
async Task WaitForContentDocumentReplacement(double previousTimeOrigin)
{
    const int attempts=50;
    for(var i=0;i<attempts;i++)
    {
        try
        {
            var frame=await page.FindFrameByNameAsync("dap-content");
            if(frame is not null)
            {
                var currentTimeOrigin=await frame.EvaluateAsync<double>("() => performance.timeOrigin");
                if(Math.Abs(currentTimeOrigin-previousTimeOrigin)>0.01)
                {
                    await WaitReady();
                    return;
                }
            }
        }
        catch(BrowserHarnessException)
        {
            // A reload can temporarily invalidate the current document.
        }

        await page.WaitForTimeoutAsync(100);
    }

    throw new TimeoutException("Content document was not replaced after the server-backed field change.");
}

async Task Select(string selector,string value)
{
    var f=await Content(); var target=f.Locator(selector);
    await MoveTo(target);
    if (visualMode) await HumanPause(300);

    // Status FieldChange performs a real server-backed document reload. Capture
    // the browser document identity before the learner action so the harness
    // cannot mistake the retiring ready document for the completed replacement.
    var waitsForDocumentReplacement=
        string.Equals(await target.GetAttributeAsync("name"),"status",StringComparison.Ordinal);
    var previousTimeOrigin=waitsForDocumentReplacement
        ? await f.EvaluateAsync<double>("() => performance.timeOrigin")
        : 0d;

    // Keep the system test deterministic: select the real option directly.
    // SelectOption fires the real change event and therefore the real CRM FieldChange flow.
    await target.SelectOptionAsync(value);
    await HumanPause(800);

    if(waitsForDocumentReplacement)
        await WaitForContentDocumentReplacement(previousTimeOrigin);
    else
        await WaitReady();
}
async Task HumanScrollTo(BrowserLocator target)
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

try
{
Console.WriteLine("DAP TestCRM representative PeopleSoft-Web scenario");
Console.WriteLine("Scenario 1: Case status FieldChange + Content iframe replacement");
Console.WriteLine("Scenario 2: Case validation failure + preservation of unsaved values");
Console.WriteLine("Scenario 3: Grid rerender/reorder + target re-resolution");
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
var expectedGuideStepCount=DapTestCrmGuideSeed.CreateSteps().Count;
if(dapSteps.Count!=expectedGuideStepCount)
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' must contain exactly {expectedGuideStepCount} Steps for the canonical Web scenario; found {dapSteps.Count}.");
if(dapSteps.Select(step=>step.Order).Distinct().Count()!=dapSteps.Count
    || dapSteps.Min(step=>step.Order)!=1
    || dapSteps.Max(step=>step.Order)!=dapSteps.Count)
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' must have contiguous unique Step orders 1..{dapSteps.Count}.");
if(dapSteps.Any(step =>
       !(step.Target?.Runtime==TargetRuntime.Web
         || (step.Target is null
             && step.Bubble.Placement==BubblePlacement.Center
             && step.AdvanceMode==StepAdvanceMode.Manual
             && step.Context is null
             && step.Validation is null
             && step.Capture is null
             && step.CompletionConditions is not { Count: > 0 }))))
    throw new Exception(
        $"DAP Guide '{DapTestCrmGuideSeed.GuideId}' contains a Step that is neither a Web target Step nor a valid centered information Step.");

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

var effectiveDapDirectory = publishedDapDirectory ?? packagedDapDirectory ?? dapOutput;
var dapStdErrLines=new System.Collections.Concurrent.ConcurrentQueue<string>();
var focusedStartStepOrder = manualFromStep ?? visualFromStep;
var bootstrapCaptures = new Dictionary<string, string>(StringComparer.Ordinal);
var resumeContextPath = Path.Combine(webRunRoot, "resume-context.json");

Process StartFocusedDap(int startStepOrder)
{
    var dapExecutable=Path.Combine(effectiveDapDirectory,"DAP.exe");
    if(!File.Exists(dapExecutable))
        throw new Exception($"DAP executable not found at {dapExecutable}");

    var resumeContextArgument=string.Empty;
    if(bootstrapCaptures.Count>0)
    {
        File.WriteAllText(resumeContextPath, JsonSerializer.Serialize(bootstrapCaptures));
        resumeContextArgument=$" --resume-context-file \"{resumeContextPath}\"";
    }

    var process=new Process
    {
        StartInfo=new ProcessStartInfo
        {
            FileName=dapExecutable,
            Arguments=$"--learner-web {DapTestCrmGuideSeed.GuideId} --start-step {startStepOrder}" +
                      resumeContextArgument,
            WorkingDirectory=effectiveDapDirectory,
            UseShellExecute=false,
            CreateNoWindow=true,
            RedirectStandardOutput=true,
            RedirectStandardError=true
        }
    };
    process.StartInfo.Environment["DAP_DATABASE_PATH"]=dapDbPath!;
    process.StartInfo.Environment["DAP_WEB_SESSION_ID"]=browser.SessionId;

    if(!process.Start())
        throw new Exception("DAP.exe process could not be started for focused Web run.");

    dapStdOutTask=process.StandardOutput.ReadToEndAsync();
    process.ErrorDataReceived+=(_,eventArgs)=>
    {
        if(eventArgs.Data is not null)
            dapStdErrLines.Enqueue(eventArgs.Data);
    };
    process.BeginErrorReadLine();

    // From this point onward the run is Guided again. Re-enable the strict
    // bubble/action identity invariant before Step N performs any learner action.
    requireActiveGuideTarget=true;

    Console.WriteLine(
        $"Web unguided bootstrap complete through Step {startStepOrder-1}; DAP started at Step {startStepOrder} with {bootstrapCaptures.Count} resume capture(s).");
    return process;
}

async Task<string?> CaptureBootstrapStepValueAsync(GuideStep step)
{
    if(step.Capture is null) return null;

    BrowserFrame frame;
    if(step.Target?.FrameContext?.Path is { Count: > 0 })
    {
        // Canonical Web TestCRM uses named single-level frames. Resolve the
        // actual live frame through the public browser surface rather than the
        // former browser-runtime helper.
        var locator=step.Target.FrameContext.Path[0];
        var name=locator.Value.Contains("dap-header",StringComparison.Ordinal)
            ? "dap-header"
            : "dap-content";
        frame=await page.FindFrameByNameAsync(name)
            ?? throw new InvalidOperationException($"Bootstrap frame '{name}' was not available.");
    }
    else
    {
        frame=page.MainFrame;
    }

    await frame.RefreshUrlAsync();
    string? raw=step.Capture.Property switch
    {
        "frame-url" => frame.Url,
        "frame-url-fragment" => new Uri(frame.Url).Fragment,
        "text" => await frame.Locator(step.Capture.Locator.Value).TextContentAsync(),
        "value" => await frame.Locator(step.Capture.Locator.Value).InputValueAsync(),
        _ => throw new NotSupportedException($"Unsupported Web bootstrap capture property '{step.Capture.Property}'.")
    };

    if(raw is null || string.IsNullOrEmpty(step.Capture.Pattern)) return raw;
    var match=System.Text.RegularExpressions.Regex.Match(
        raw,
        step.Capture.Pattern,
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    if(!match.Success) return null;
    return match.Groups.Count>1 ? match.Groups[1].Value : match.Value;
}

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

    if(focusedStartStepOrder is not null && order<focusedStartStepOrder.Value)
    {
        if(expected.Capture is not null)
        {
            var captured=await CaptureBootstrapStepValueAsync(expected);
            if(string.IsNullOrWhiteSpace(captured))
                throw new InvalidOperationException(
                    $"Web bootstrap could not capture runtime value for Step {order} '{expected.Id}'.");

            bootstrapCaptures[expected.Id]=captured;
            Console.WriteLine($"Web unguided bootstrap captured Step {order}: {expected.Id}");
        }
        else if(advancedSequence)
        {
            Console.WriteLine($"Web unguided bootstrap Step {order}/{dapSteps.Count}: {expected.Id}");
        }
        return;
    }

    if(dapProcess is null)
    {
        if(focusedStartStepOrder!=order)
            throw new InvalidOperationException(
                $"Web DAP launch expected at Step {focusedStartStepOrder}, but scenario reached Step {order}.");

        dapProcess=StartFocusedDap(order);
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
                foreach(var selector in new[] { "#dap-guide-bubble", "#dap-guide-bubble-proxy", "#dap-guide-centered" })
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
                                Console.WriteLine($"E2E mode transition: UNGUIDED -> VISUAL at Step {order}");
                            }
                            await HumanPause(500);
                            if(manualFromStep == order)
                            {
                                Console.WriteLine();
                                Console.WriteLine($"MANUAL HANDOFF: Step {order} is ready.");
                                Console.WriteLine("Automatic learner actions are paused. Continue manually in the browser by following the DAP bubbles.");
                                Console.WriteLine("The run will close automatically when DAP completes the Guide or the owned browser/page is closed.");
                                Console.WriteLine("Press Ctrl+C only if you want to stop the run early.");

                                var dapExit=dapProcess!.WaitForExitAsync();
                                var webHostExit=ownedTestCrmProcess!.WaitForExitAsync();
                                var completed=await Task.WhenAny(
                                    dapExit,
                                    ownedWebTargetClosed.Task,
                                    webHostExit);

                                if(completed==dapExit)
                                {
                                    await dapExit;
                                    if(dapProcess.ExitCode!=0)
                                        throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} during the manual Web From-Step run.");

                                    Console.WriteLine("DAP completed the manual Web From-Step Guide. Cleaning up E2E-owned processes.");
                                }
                                else if(completed==webHostExit)
                                {
                                    await webHostExit;
                                    throw new Exception(
                                        $"TestCRM Web host exited unexpectedly during the manual Web From-Step run. ExitCode={ownedTestCrmProcess.ExitCode}.");
                                }
                                else
                                {
                                    var reason=await ownedWebTargetClosed.Task;
                                    Console.WriteLine(
                                        $"Owned Web target closed ({reason}). Ending the manual Web From-Step run and cleaning up owned processes.");
                                }

                                throw new ManualWebHandoffCompleteException();
                            }
                            return;
                        }
                    }
                }
            }
            catch(BrowserHarnessException) { }
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

if(!unguided && focusedStartStepOrder is null)
{
var dapStep=dapSteps[0];
var dapSecondStep=dapSteps[1];
var dapExecutable=Path.Combine(effectiveDapDirectory,"DAP.exe");
if(!File.Exists(dapExecutable))
    throw new Exception($"DAP executable not found at {dapExecutable}");

dapProcess=new Process
{
    StartInfo=new ProcessStartInfo
    {
        FileName=dapExecutable,
        Arguments=$"--learner-web {DapTestCrmGuideSeed.GuideId}",
        WorkingDirectory=effectiveDapDirectory,
        UseShellExecute=false,
        CreateNoWindow=true,
        RedirectStandardOutput=true,
        RedirectStandardError=true
    }
};
dapProcess.StartInfo.Environment["DAP_DATABASE_PATH"]=dapDbPath!;
dapProcess.StartInfo.Environment["DAP_WEB_SESSION_ID"]=browser.SessionId;
var dapStartupTimer=Stopwatch.StartNew();
if(!dapProcess.Start())
    throw new Exception("DAP.exe process could not be started.");
StartupMark("DAP.exe process started");

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

if (manual)
{
    Console.WriteLine();
    Console.WriteLine("MANUAL WEB RUN: Step 1 is ready.");
    Console.WriteLine("Automatic learner actions are disabled. Perform the full Guide manually in the browser.");
    Console.WriteLine("The run will close automatically when DAP completes the Guide or you close the owned browser/page.");
    Console.WriteLine("Press Ctrl+C only if you want to stop the run early.");

    var browserDisconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    void OnManualBrowserDisconnected(object? _, EventArgs __) => browserDisconnected.TrySetResult("browser-disconnected");
    browser.Disconnected += OnManualBrowserDisconnected;

    try
    {
        var dapExit = dapProcess.WaitForExitAsync();
        var webHostExit = ownedTestCrmProcess!.WaitForExitAsync();

        var completed = await Task.WhenAny(
            dapExit,
            browserDisconnected.Task,
            webHostExit);

        if (completed == dapExit)
        {
            await dapExit;
            if (dapProcess.ExitCode != 0)
                throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} during the manual Web learner run.");

            Console.WriteLine("DAP completed the manual Web Guide. Closing E2E-owned processes.");
        }
        else if (completed == webHostExit)
        {
            await webHostExit;
            throw new Exception(
                $"TestCRM Web host exited unexpectedly during the manual learner run. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                $"STDOUT tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdOut)}{Environment.NewLine}" +
                $"STDERR tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdErr)}");
        }
        else
        {
            Console.WriteLine("Owned Web browser closed. Ending the manual learner run and cleaning up owned processes.");
        }
    }
    finally
    {
        browser.Disconnected -= OnManualBrowserDisconnected;
    }

    return;
}

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

var exactValueTarget=(await Content()).Locator("[name='name']");
await MoveTo(exactValueTarget);
await exactValueTarget.ClickAsync();
await page.Keyboard.PressAsync("Control+A");
await page.Keyboard.TypeAsync("אלפא פתרונות בע\"מ");
await page.WaitForTimeoutAsync(350);

// Reaching the valid value is not itself a text-edit commit. Step 1 must stay
// active until the learner leaves the field and the Runtime receives blur.
dapContent=await Content();
var preBlurBubble=dapContent.Locator("#dap-guide-bubble");
if(await preBlurBubble.CountAsync()!=1
    || !(await preBlurBubble.TextContentAsync() ?? string.Empty).Contains(dapStep.Bubble.Content,StringComparison.Ordinal))
    throw new Exception("Step 1 advanced before the text edit was committed by leaving the field.");
Console.WriteLine("DAP text validation waits for blur before advancing: PASS");

await page.Keyboard.PressAsync("Tab");
await HumanPause(120);

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
    // Unguided runs never launch DAP. Focused From-Step runs use the same
    // business actions as an unguided bootstrap until the requested Step,
    // where WaitForGuideStep launches DAP with the captured resume context.
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

// Sorting is a visible learner action in visual mode, so it has its own Guide Step.
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

// Continue only with the next real learner action from the persisted Guide.
// The canonical runner never injects a technical browser reload between Steps.
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
if(!unguided && dapProcess is not null)
{
    var informationConfirm=page.Locator("#dap-guide-centered [data-dap-guide-confirm='1']");
    await informationConfirm.WaitForAsync(new() { State = BrowserWaitState.Visible, Timeout = 5000 });
    if(visualMode)
    {
        await page.WaitForTimeoutAsync(500);
        await MoveTo(informationConfirm, enforceActiveGuideTarget: false);
    }
    await informationConfirm.ClickAsync();
}

await WaitForGuideStep(52);
await Click("#delete-case");
frame=await Content();
var confirmDelete=frame.Locator("#ps-confirm [data-answer='yes']");
await confirmDelete.WaitForAsync();
await WaitForGuideStep(53);
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
var headerFrame=await page.FindFrameByNameAsync("dap-header")
    ?? throw new Exception("Header frame was not found.");
var header=headerFrame.Locator("#portal-header");
await WaitForGuideStep(54);
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

if(!unguided)
{
    var completionBubble=page.Locator("#dap-guide-completed");
    await completionBubble.WaitForAsync(new() { State = BrowserWaitState.Visible, Timeout = 5000 });
    var finishButton=completionBubble.Locator("[data-dap-guide-finish='1']");
    await finishButton.WaitForAsync(new() { State = BrowserWaitState.Visible, Timeout = 5000 });
    if(visualMode)
    {
        await page.WaitForTimeoutAsync(800);
        await MoveTo(finishButton, enforceActiveGuideTarget: false);
    }
    await finishButton.ClickAsync();

    if(dapProcess is null)
        throw new Exception("DAP.exe process is missing while completing the Guided Web run.");
    if(!dapProcess.WaitForExit(5000))
        throw new TimeoutException("DAP.exe did not complete after the Web completion Finish action.");
    if(dapProcess.ExitCode!=0)
        throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} after Web completion.");
}

if(lastScenarioGuideOrder!=dapSteps.Count)
    throw new Exception(
        $"Canonical Web scenario completed after Guide Step {lastScenarioGuideOrder}; expected {dapSteps.Count}.");

Console.WriteLine(unguided
    ? $"PASS: Web unguided executed the canonical {dapSteps.Count}-step scenario sequenced by the persisted Guide in DAP.db, without DAP.exe or bubbles."
    : "PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.");
await page.WaitForTimeoutAsync(visualMode ? 1500 : 0);
}
catch (ManualWebHandoffCompleteException)
{
    Console.WriteLine("Manual Web From-Step run finished.");
}
catch (Exception) when (ownedWebTargetClosed.Task.IsCompleted)
{
    var closeReason = await ownedWebTargetClosed.Task;
    Console.WriteLine($"Owned Web target closed ({closeReason}). Ending the run and cleaning up owned processes.");
}
finally
{
    browser.Disconnected -= OnOwnedBrowserDisconnected;

    AppDomain.CurrentDomain.ProcessExit -= webProcessExitCleanup;
    Console.CancelKeyPress -= webCancelCleanup;

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

    try
    {
        if (Directory.Exists(webRunRoot))
            Directory.Delete(webRunRoot, recursive: true);
    }
    catch (IOException)
    {
        // A hard interruption can leave an isolated run directory temporarily
        // locked. Future runs never reuse it, so it cannot block later builds.
    }
    catch (UnauthorizedAccessException)
    {
    }
}

sealed class ManualWebHandoffCompleteException : Exception
{
}

readonly record struct BrowserWindowMetrics(
    double ScreenX,
    double ScreenY,
    double OuterWidth,
    double OuterHeight,
    double InnerWidth,
    double InnerHeight);

[StructLayout(LayoutKind.Sequential)]
struct NativePoint
{
    public int X;
    public int Y;
}

static class NativeCursor
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint point);
}
