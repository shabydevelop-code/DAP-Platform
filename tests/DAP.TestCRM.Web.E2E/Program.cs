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
int? fastFromStep = null;
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

    if (args[i].Equals("--fast-from-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsedFastStep) || parsedFastStep < 1)
            throw new ArgumentException("--fast-from-step requires a positive Guide Step order.");
        fastFromStep = parsedFastStep;
        continue;
    }

    if (args[i].Equals("--visual-from-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsedVisualStep) || parsedVisualStep < 1)
            throw new ArgumentException("--visual-from-step requires a positive Guide Step order.");
        visualFromStep = parsedVisualStep;
    }
}

var focusedModeCount = new[] { manualFromStep, fastFromStep, visualFromStep }.Count(step => step is not null);
if (focusedModeCount > 1)
    throw new ArgumentException("--manual-from-step, --fast-from-step, and --visual-from-step cannot be combined.");

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
var explicitFast = args.Contains("--fast", StringComparer.OrdinalIgnoreCase);
var explicitVisual = args.Contains("--visual", StringComparer.OrdinalIgnoreCase);
if (explicitFast && explicitVisual)
    throw new ArgumentException("--fast and --visual cannot be combined.");
if ((explicitFast || explicitVisual) && !explicitGuided)
    throw new ArgumentException("--fast and --visual require --guided.");
if (unguided && explicitGuided)
    throw new ArgumentException("--guided and --unguided cannot be combined.");
if (manual && (unguided || explicitGuided || manualFromStep is not null || fastFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--manual cannot be combined with --guided, --unguided, or a from-step mode.");
if (unguided && (manualFromStep is not null || fastFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--unguided cannot be combined with a from-step mode.");

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

        // Unguided now runs the production DAP Runtime with presentation
        // suppressed, so every non-diagnostic E2E mode requires DAP.exe.
        if (publishedDapDirectory is null)
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

// Public run mode is determined only by command-line switches.
// A full guided run defaults to fast unless --visual is explicit.
var e2eMode = explicitVisual ? "visual" : "fast";

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
Console.WriteLine($"E2E mode: {(manual ? "manual" : unguided ? "unguided" : manualFromStep is not null ? $"unguided -> manual from Step {manualFromStep}" : fastFromStep is not null ? $"unguided -> fast from Step {fastFromStep}" : visualFromStep is not null ? $"unguided -> visual from Step {visualFromStep}" : visualMode ? "visual" : "fast")}");
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
async Task MoveTo(BrowserLocator target)
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
    var f=await Content();
    var target=f.Locator(selector);
    await MoveTo(target);
    await target.ClickAsync();
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
async Task Select(string selector,string value)
{
    var f=await Content();
    var target=f.Locator(selector);
    await MoveTo(target);
    if (visualMode) await HumanPause(300);
    await target.SelectOptionAsync(value);
    await HumanPause(800);
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
// Every mode runs the production DAP Runtime from Step 1 against the persisted
// Guide in DAP.db. Unguided/focused bootstrap modes suppress presentation only;
// target resolution, validation, capture and completion remain Runtime-owned.
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
if(fastFromStep is not null && !dapSteps.Any(step => step.Order == fastFromStep.Value))
    throw new ArgumentOutOfRangeException(
        nameof(fastFromStep),
        fastFromStep,
        $"Guide '{DapTestCrmGuideSeed.GuideId}' does not contain Step {fastFromStep}.");
if(visualFromStep is not null && !dapSteps.Any(step => step.Order == visualFromStep.Value))
    throw new ArgumentOutOfRangeException(
        nameof(visualFromStep),
        visualFromStep,
        $"Guide '{DapTestCrmGuideSeed.GuideId}' does not contain Step {visualFromStep}.");

var effectiveDapDirectory = publishedDapDirectory ?? packagedDapDirectory ?? dapOutput;
var dapStdErrLines=new System.Collections.Concurrent.ConcurrentQueue<string>();
var focusedStartStepOrder = manualFromStep ?? fastFromStep ?? visualFromStep;
var bootstrapCaptures = new Dictionary<string, string>(StringComparer.Ordinal);
var resumeContextPath = Path.Combine(webRunRoot, "resume-context.json");

Process StartFocusedDap(int? showGuidanceFromStepOrder, bool hideGuidance = false)
{
    var dapExecutable=Path.Combine(effectiveDapDirectory,"DAP.exe");
    if(!File.Exists(dapExecutable))
        throw new Exception($"DAP executable not found at {dapExecutable}");

    var guidanceArgument = hideGuidance
        ? " --hide-guidance"
        : showGuidanceFromStepOrder is not null
            ? $" --show-guidance-from-step {showGuidanceFromStepOrder.Value}"
            : string.Empty;

    var process=new Process
    {
        StartInfo=new ProcessStartInfo
        {
            FileName=dapExecutable,
            Arguments=$"--learner-web {DapTestCrmGuideSeed.GuideId}" + guidanceArgument,
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
        {
            dapStdErrLines.Enqueue(eventArgs.Data);
        }
    };
    process.BeginErrorReadLine();

    Console.WriteLine(hideGuidance
        ? "Web DAP Runtime started with guidance hidden."
        : $"Web DAP Runtime started at Step 1 with guidance hidden through Step {showGuidanceFromStepOrder!.Value - 1}.");
    return process;
}

var lastScenarioGuideOrder=0;

async Task WaitForGuideStep(int order)
{
    var expected=dapSteps.Single(step=>step.Order==order);

    if(order<lastScenarioGuideOrder || order>lastScenarioGuideOrder+1)
        throw new Exception(
            $"Canonical Web scenario requested Guide Step {order} after Step {lastScenarioGuideOrder}; expected Step {lastScenarioGuideOrder} or {lastScenarioGuideOrder+1}.");

    var advancedSequence=order==lastScenarioGuideOrder+1;
    if(advancedSequence)
        lastScenarioGuideOrder=order;

    var startMarker=$"[DAP guide] starting Step {order}/{dapSteps.Count} '{expected.Id}'";
    var deadline=DateTime.UtcNow.AddSeconds(5);
    while(DateTime.UtcNow<deadline)
    {
        if(dapStdErrLines.Any(line=>line.Contains(startMarker,StringComparison.Ordinal)))
        {
            if(visualFromStep == order && !switchedToVisual)
            {
                visualMode=true;
                fastMode=false;
                switchedToVisual=true;
                Console.WriteLine($"E2E mode transition: hidden -> VISUAL at Step {order}");
            }

            if(manualFromStep == order)
            {
                Console.WriteLine();
                Console.WriteLine($"MANUAL HANDOFF: Runtime reached Step {order}.");
                Console.WriteLine("Automatic learner actions are paused. Continue manually in the browser.");
                Console.WriteLine("The run will close automatically when DAP completes the Guide or the owned browser/page is closed.");

                var dapExit=dapProcess!.WaitForExitAsync();
                var webHostExit=ownedTestCrmProcess!.WaitForExitAsync();
                var completed=await Task.WhenAny(dapExit,ownedWebTargetClosed.Task,webHostExit);

                if(completed==dapExit)
                {
                    await dapExit;
                    if(dapProcess.ExitCode!=0)
                        throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} during the manual Web From-Step run.");
                }
                else if(completed==webHostExit)
                {
                    await webHostExit;
                    throw new Exception(
                        $"TestCRM Web host exited unexpectedly during the manual Web From-Step run. ExitCode={ownedTestCrmProcess.ExitCode}.");
                }

                throw new ManualWebHandoffCompleteException();
            }

            return;
        }

        if(dapProcess is not null && dapProcess.HasExited)
            throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} before Runtime reached Step {order}.");

        await page.WaitForTimeoutAsync(50);
    }

    var recentDapDiagnostics=string.Join(
        Environment.NewLine,
        dapStdErrLines.Where(line=>
            line.StartsWith("[DAP guide]",StringComparison.Ordinal)
            || line.StartsWith("[DAP validation]",StringComparison.Ordinal)
            || line.StartsWith("[DAP bubble]",StringComparison.Ordinal)
            || line.StartsWith("[DAP runtime]",StringComparison.Ordinal)
            || line.StartsWith("[DAP runtime trace]",StringComparison.Ordinal)));

    throw new TimeoutException(
        $"DAP Runtime did not reach Step {order}: {expected.Id} within 5 seconds.{Environment.NewLine}" +
        $"DAP diagnostics:{Environment.NewLine}{recentDapDiagnostics}");
}

if(unguided)
{
    dapProcess=StartFocusedDap(null, hideGuidance: true);
}
else if(focusedStartStepOrder is not null)
{
    dapProcess=StartFocusedDap(focusedStartStepOrder.Value);
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

}

// Automatic regression runner: replace only the learner's hands.
// Runtime + persisted Guide own target resolution, validation, completion and Step advancement.
await WaitForGuideStep(1);
await Fill("[name='name']","אלפא פתרונות בע\"מ");

await WaitForGuideStep(2);
await Click("#customer-search button.primary");

await WaitForGuideStep(3);
await Click("#search-results tbody tr.clickable:first-child");

await WaitForGuideStep(4);
await Click("tbody tr.clickable:has-text('מטה תל אביב')");

await WaitForGuideStep(5);
await Click("nav.tabs button:has-text('פניות')");

await WaitForGuideStep(6);
await Click("th button[data-sort='status']");

await WaitForGuideStep(7);
await Click("button.primary:has-text('פניה חדשה')");

await WaitForGuideStep(8);
await Fill("[name='subject']","תקלה בחיבור לאינטרנט");

await WaitForGuideStep(9);
await Fill("[name='description']","הלקוח מדווח על חיבור לא יציב.");

await WaitForGuideStep(10);
await Click("button.primary:has-text('שמור')");

await WaitForGuideStep(11);
// Remember only the identity of the record the learner just created so later
// learner actions can reopen that same visible record. This does not determine
// Step completion or advancement.
var createdCaseFrame=await Content();
var createdCaseUrl=createdCaseFrame.Url;
var caseMarker="#/case/";
var casePos=createdCaseUrl.IndexOf(caseMarker,StringComparison.Ordinal);
if(casePos<0)
    throw new Exception("Could not identify the Case created by the learner action.");
var createdCaseId=createdCaseUrl[(casePos+caseMarker.Length)..].Split('?', '/', '#')[0];
await Click(".breadcrumb a:nth-of-type(3)");

await WaitForGuideStep(12);
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");

await WaitForGuideStep(13);
await Select("[name='status']","בטיפול");

await WaitForGuideStep(14);
await Fill("[name='resolutionNotes']","בוצעה בדיקת שירות מול הלקוח והתקלה טופלה.");

await WaitForGuideStep(15);
var activityFrame=await Content();
var activityMore=activityFrame.Locator("#activity-more");
await HumanScrollTo(activityMore);
await MoveTo(activityMore);
await activityMore.ClickAsync();

await WaitForGuideStep(16);
await Select("[name='status']","סגורה");

await WaitForGuideStep(17);
await Fill("[name='subject']","תקלה בחיבור לאינטרנט");

await WaitForGuideStep(18);
await Click("button.primary:has-text('שמור')");

await WaitForGuideStep(19);
await Click("#ps-alert button");

await WaitForGuideStep(20);
await Select("[name='closeReason']","טופל");

await WaitForGuideStep(21);
await Click("button.primary:has-text('שמור')");

await WaitForGuideStep(22);
await Click(".breadcrumb a[data-go^='#/site/']");

await WaitForGuideStep(23);
await Click("nav.tabs button:has-text('לידים')");

await WaitForGuideStep(24);
await Click("nav.tabs button:has-text('פניות')");

await WaitForGuideStep(25);
await Click("nav.tabs button:has-text('לידים')");

await WaitForGuideStep(26);
await Click("button.primary:has-text('ליד חדש')");

await WaitForGuideStep(27);
await Fill("[name='contactName']","לקוח בדיקת מערכת");

await WaitForGuideStep(28);
await Click("button.primary:has-text('שמור')");

await WaitForGuideStep(29);
await Select("[name='status']","נסגר בהצלחה");

await WaitForGuideStep(30);
await Select("[name='status']","חדש");

await WaitForGuideStep(31);
await Select("[name='status']","נסגר בהצלחה");

await WaitForGuideStep(32);
await Click("button.primary:has-text('שמור')");

await WaitForGuideStep(33);
await Click("#ps-alert button");

await WaitForGuideStep(34);
await Select("[name='selectedService']","תמיכה מורחבת");

await WaitForGuideStep(35);
await Click("button.primary:has-text('שמור')");

await WaitForGuideStep(36);
await Click("#delete-lead");

await WaitForGuideStep(37);
await Click("#ps-confirm [data-answer='yes']");

await WaitForGuideStep(38);
await Click(".breadcrumb a[data-go^='#/customer/']");

await WaitForGuideStep(39);
await Click("tbody tr.clickable:has-text('מטה תל אביב')");

await WaitForGuideStep(40);
await Click("nav.tabs button:has-text('לידים')");

await WaitForGuideStep(41);
await Click("tbody tr.clickable:has-text('אבי כהן')");

await WaitForGuideStep(42);
await Select("[name='status']","חדש");

await WaitForGuideStep(43);
await Select("[name='status']","נסגר בהצלחה");

await WaitForGuideStep(44);
await Select("[name='status']","חדש");

await WaitForGuideStep(45);
await Select("[name='status']","נסגר בהצלחה");

await WaitForGuideStep(46);
await Click(".breadcrumb a[data-go^='#/site/'][data-go$='/leads']");

await WaitForGuideStep(47);
await Click("nav.tabs button:has-text('פניות')");

await WaitForGuideStep(48);
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");

await WaitForGuideStep(49);
await Click(".breadcrumb a[data-go^='#/site/']");

await WaitForGuideStep(50);
await Click($"button.grid-open[data-go='#/case/{createdCaseId}']");

await WaitForGuideStep(51);
if(!unguided && dapProcess is not null)
{
    var informationConfirm=page.Locator("#dap-guide-centered [data-dap-guide-confirm='1']");
    await MoveTo(informationConfirm);
    await informationConfirm.ClickAsync();
}

await WaitForGuideStep(52);
await Click("#delete-case");

await WaitForGuideStep(53);
await Click("#ps-confirm [data-answer='yes']");

await WaitForGuideStep(54);
var headerFrame=await page.FindFrameByNameAsync("dap-header")
    ?? throw new Exception("Header frame was not found for the learner action.");
var header=headerFrame.Locator("#portal-header");
await MoveTo(header);
await header.ClickAsync();

await WaitForGuideStep(55);
if(!unguided && dapProcess is not null)
{
    var summaryConfirm=page.Locator("#dap-guide-centered [data-dap-guide-confirm='1']");
    await MoveTo(summaryConfirm);
    await summaryConfirm.ClickAsync();
}

if(lastScenarioGuideOrder!=dapSteps.Count)
    throw new Exception(
        $"Canonical Web scenario completed after Guide Step {lastScenarioGuideOrder}; expected {dapSteps.Count}.");

if(unguided)
{
    if(dapProcess is null)
        throw new Exception("DAP.exe process is missing while completing the unguided Web run.");
    if(!dapProcess.WaitForExit(5000))
    {
        var finalRuntimeDiagnostics=string.Join(
            Environment.NewLine,
            dapStdErrLines.Where(line =>
                line.StartsWith("[DAP guide]",StringComparison.Ordinal)
                || line.StartsWith("[DAP validation]",StringComparison.Ordinal)
                || line.StartsWith("[DAP bubble]",StringComparison.Ordinal)
                || line.StartsWith("[DAP runtime]",StringComparison.Ordinal)
                || line.StartsWith("[DAP runtime trace]",StringComparison.Ordinal)));
        throw new TimeoutException(
            $"DAP Runtime did not complete the unguided Guide within 5 seconds after the final learner action.{Environment.NewLine}" +
            $"DAP diagnostics:{Environment.NewLine}{finalRuntimeDiagnostics}");
    }
    if(dapProcess.ExitCode!=0)
        throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} during the unguided Web run.");
}

Console.WriteLine(unguided
    ? $"PASS: Web unguided executed the canonical {dapSteps.Count}-step scenario through DAP Runtime and the persisted Guide in DAP.db, with guidance hidden."
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
