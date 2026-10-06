using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;
using DAP.TestCRM.E2E.Common;
using DAP.TestCRM.Windows.E2E;

const string appTitle = "DAP Test CRM - Windows";
const string mainWindowAutomationId = "TestCrmMainWindow";
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var appProject = Path.Combine(root, "test-apps", "DAP.TestCRM", "Windows", "DAP.TestCRM.Windows.csproj");
var backendProject = Path.Combine(root, "test-apps", "DAP.TestCRM", "Server", "DAP.TestCRM.Server.csproj");
var dapProject = Path.Combine(root, "src", "DAP.App", "DAP.App.csproj");
var appProjectDirectory = Path.GetDirectoryName(appProject)!;
var backendProjectDirectory = Path.GetDirectoryName(backendProject)!;
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
var packagedWindowsDirectory = packagedDiagnostics ? Path.Combine(diagnosticsRoot!, "TestCRM", "Windows") : null;
var packagedDapDirectory = packagedDiagnostics ? Path.GetFullPath(Path.Combine(diagnosticsRoot!, "..")) : null;

if (args.Contains("--reset-guide", StringComparer.OrdinalIgnoreCase))
{
    var options = SqliteDatabaseOptions.CreateDefault();
    var factory = new SqliteConnectionFactory(options);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);
    var resetSteps = DapTestCrmWindowsGuideSeed.CreateSteps();
    await repository.ReplaceStepsAsync(DapTestCrmWindowsGuideSeed.GuideId, resetSteps);

    await repository.RenameGuideAsync(
        DapTestCrmWindowsGuideSeed.GuideId,
        DapTestCrmWindowsGuideSeed.GuideId,
        DapTestCrmWindowsGuideSeed.GuideName);

    Console.WriteLine(
        $"Reset Guide '{DapTestCrmWindowsGuideSeed.GuideId}' " +
        $"({resetSteps.Count} steps) in {options.DatabasePath}");
    return;
}

string? publishedDapDirectory = null;
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].Equals("--published-dap", StringComparison.OrdinalIgnoreCase))
        continue;

    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
        throw new ArgumentException("--published-dap requires a directory containing DAP.exe.");

    publishedDapDirectory = Path.GetFullPath(args[++i]);
}

var manual = args.Contains("--manual", StringComparer.OrdinalIgnoreCase);
var hybrid = args.Contains("--hybrid", StringComparer.OrdinalIgnoreCase);
if (manual == hybrid)
    throw new ArgumentException("Choose exactly one Windows run mode: --manual or --hybrid.");

await RunGuidedAsync(fullManual: manual, hybrid: hybrid);
return;

async Task RunGuidedAsync(
    bool fullManual = false,
    bool hybrid = false)
{
    var databaseOptions = SqliteDatabaseOptions.CreateDefault();
    var factory = new SqliteConnectionFactory(databaseOptions);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);
    var persistedSteps = await repository.GetStepsAsync(DapTestCrmWindowsGuideSeed.GuideId);
    var expectedStepCount = DapTestCrmWindowsGuideSeed.CreateSteps().Count;
    if (persistedSteps.Count != expectedStepCount)
    {
        throw new InvalidOperationException(
            $"Guide '{DapTestCrmWindowsGuideSeed.GuideId}' contains {persistedSteps.Count} persisted Steps, " +
            $"but the current seed defines {expectedStepCount}. " +
            "Run this project once with --reset-guide first.");
    }

    Process? backend = null;
    Process? windowsApp = null;
    Process? dap = null;
    var runRoot = Path.Combine(
        Path.GetTempPath(),
        "DAP",
        "E2E",
        "Windows",
        Guid.NewGuid().ToString("N"));
    var backendOutput = Path.Combine(runRoot, "Server");
    var windowsOutput = Path.Combine(runRoot, "Windows");
    var dapOutput = Path.Combine(runRoot, "DAP");

    void KillOwnedChildren()
    {
        TryKillOwnedProcessTree(dap);
        TryKillOwnedProcessTree(windowsApp);
        TryKillOwnedProcessTree(backend);
    }

    EventHandler processExitCleanup = (_, _) => KillOwnedChildren();
    ConsoleCancelEventHandler cancelCleanup = (_, _) => KillOwnedChildren();
    AppDomain.CurrentDomain.ProcessExit += processExitCleanup;
    Console.CancelKeyPress += cancelCleanup;

    try
    {
        EnsurePortFree(5201);

        if (!packagedDiagnostics)
        {
            BuildIsolated(backendProject, backendOutput, "TestCRM Server");
            BuildIsolated(appProject, windowsOutput, "TestCRM Windows");
            if (publishedDapDirectory is null)
                BuildIsolated(dapProject, dapOutput, "DAP");
        }

        var effectiveBackendDirectory = packagedDiagnostics ? packagedServerDirectory! : backendOutput;
        var effectiveWindowsDirectory = packagedDiagnostics ? packagedWindowsDirectory! : windowsOutput;
        var backendDll = Path.Combine(effectiveBackendDirectory, "DAP.TestCRM.Server.dll");
        var windowsExe = Path.Combine(effectiveWindowsDirectory, "DAP.TestCRM.Windows.exe");
        var effectiveDapDirectory = publishedDapDirectory ?? packagedDapDirectory ?? dapOutput;
        var dapExe = Path.Combine(effectiveDapDirectory, "DAP.exe");
        if (!File.Exists(dapExe))
            throw new FileNotFoundException("Published DAP.exe was not found.", dapExe);

        backend = StartProcess(
            "dotnet",
            $"\"{backendDll}\"",
            new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://localhost:5201" },
            workingDirectory: effectiveBackendDirectory);

        await WaitForHttpAsync("http://localhost:5201/api/customers", backend, "TestCRM backend");

        windowsApp = StartProcess(
            windowsExe,
            string.Empty,
            workingDirectory: effectiveWindowsDirectory);

        var window = WaitForMainWindow();
        var customerName = WaitForElementById(window, "CustomerNameSearch");

        Process StartDap()
        {
            var process = StartProcess(
                dapExe,
                $"--learner-windows {DapTestCrmWindowsGuideSeed.GuideId} " +
                $"--window-automation-id {mainWindowAutomationId}" +
                (hybrid ? " --hybrid-presentation" : string.Empty),
                redirectOutput: true,
                workingDirectory: effectiveDapDirectory);

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine($"[DAP] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.Error.WriteLine($"[DAP STDERR] {e.Data}");
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        dap = StartDap();
        var driver = new WindowsCrmScenarioDriver(windowsApp, window);

        Console.WriteLine(fullManual ? "E2E mode: manual" : "E2E mode: hybrid");

        try
        {
            if (fullManual)
            {
                var firstEnabled = persistedSteps.OrderBy(step => step.Order).First(step => step.IsEnabled);
                WaitForBubble(firstEnabled.Bubble.Content, dap);

                Console.WriteLine();
                Console.WriteLine($"MANUAL WINDOWS RUN: Step {firstEnabled.Order}/{persistedSteps.Count} is ready.");
                Console.WriteLine("Continue manually in TestCRM by following the DAP bubbles.");

                await dap.WaitForExitAsync();
                if (dap.ExitCode != 0)
                    throw new Exception($"DAP.exe exited with code {dap.ExitCode} during the manual learner run.");

                Console.WriteLine("PASS: manual Windows Guide completed.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("HYBRID WINDOWS RUN: Runtime owns the Guide; persisted AutomationValue data fills value controls.");
            Console.WriteLine("Buttons, navigation, dialogs, and centered information remain manual learner actions.");

            foreach (var step in persistedSteps.OrderBy(step => step.Order))
            {
                if (!step.IsEnabled)
                {
                    Console.WriteLine($"HYBRID: skipping disabled persisted Step {step.Order} '{step.Id}'.");
                    continue;
                }

                WaitForBubbleWithoutHumanTimeout(step.Bubble.Content, dap);

                if (string.IsNullOrEmpty(step.AutomationValue))
                    continue;

                driver.SetActiveGuideStep(step.Order, step.Id);
                await driver.ApplyAutomationValue(step);
            }

            await dap.WaitForExitAsync();
            if (dap.ExitCode != 0)
                throw new Exception($"DAP.exe exited with code {dap.ExitCode} during the hybrid Windows run.");

            Console.WriteLine("PASS: hybrid Windows Guide completed.");
            return;
        }
        catch (TargetApplicationClosedException)
        {
            Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
        }
        catch (Exception) when (windowsApp is not null && windowsApp.HasExited)
        {
            Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
        }
    }
    finally
    {
        AppDomain.CurrentDomain.ProcessExit -= processExitCleanup;
        Console.CancelKeyPress -= cancelCleanup;

        if (dap is not null) StopOwnedProcessTree(dap);
        if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
        if (backend is not null) StopOwnedProcessTree(backend);

        try
        {
            if (Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
        }
        catch (IOException)
        {
            // A hard process termination can leave the isolated run directory
            // temporarily locked. It is safe to leave because no later run reuses it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

void BuildIsolated(string project, string output, string name)
{
    Directory.CreateDirectory(output);
    Console.WriteLine($"Building {name} into isolated E2E output: {output}");

    using var build = Process.Start(new ProcessStartInfo(
        "dotnet",
        $"build \"{project}\" --nologo --verbosity minimal --output \"{output}\"")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }) ?? throw new InvalidOperationException($"Could not start isolated build for {name}.");

    var stdout = build.StandardOutput.ReadToEndAsync();
    var stderr = build.StandardError.ReadToEndAsync();
    build.WaitForExit();

    if (build.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"{name} isolated build failed with exit code {build.ExitCode}.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{stdout.GetAwaiter().GetResult()}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{stderr.GetAwaiter().GetResult()}");
    }
}

void EnsurePortFree(int port)
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
            $"Port {port} is already in use. Stop the existing TestCRM Server before running this E2E mode.",
            ex);
    }
    finally
    {
        listener?.Stop();
    }
}

Process StartProcess(
    string fileName,
    string arguments,
    IReadOnlyDictionary<string, string?>? environment = null,
    bool redirectOutput = false,
    string? workingDirectory = null)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory ?? root,
        UseShellExecute = false,
        RedirectStandardOutput = redirectOutput,
        RedirectStandardError = redirectOutput
    };

    if (environment is not null)
        foreach (var pair in environment)
            psi.Environment[pair.Key] = pair.Value;

    return Process.Start(psi)
        ?? throw new InvalidOperationException($"Could not start process: {fileName} {arguments}");
}

AutomationElement WaitForMainWindow(int timeout = 30_000)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeout)
    {
        var window = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, appTitle),
                new PropertyCondition(AutomationElement.AutomationIdProperty, mainWindowAutomationId)));

        if (window is not null)
            return window;

        Thread.Sleep(100);
    }

    throw new TimeoutException("Timed out waiting for Windows TestCRM main window.");
}

AutomationElement WaitForElementById(
    AutomationElement rootElement,
    string automationId,
    int timeout = 10_000)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeout)
    {
        try
        {
            var element = rootElement.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (element is not null)
                return element;
        }
        catch (ElementNotAvailableException)
        {
        }

        Thread.Sleep(100);
    }

    throw new TimeoutException($"Timed out waiting for UIA element '{automationId}'.");
}

AutomationElement WaitForBubbleWithoutHumanTimeout(string expectedInstruction, Process dapProcess)
{
    while (true)
    {
        if (dapProcess.HasExited)
        {
            if (dapProcess.ExitCode == 0)
                throw new InvalidOperationException(
                    $"DAP.exe completed before expected Windows Step bubble '{expectedInstruction}' was observed.");

            throw new Exception(
                $"DAP.exe exited with code {dapProcess.ExitCode} before Windows Step bubble '{expectedInstruction}' was observed.");
        }

        try
        {
            return WaitForBubble(expectedInstruction, dapProcess, timeout: 1_000);
        }
        catch (TimeoutException)
        {
            // Hybrid manual actions have no human-response deadline. The
            // one-second probe is only a technical observation interval.
        }
    }
}

AutomationElement WaitForBubble(string expectedInstruction, Process dapProcess, int timeout = 5_000)
{
    var sw = Stopwatch.StartNew();
    string? lastObservedInstruction = null;
    var diagnosticLogged = false;

    while (sw.ElapsedMilliseconds < timeout)
    {
        if (dapProcess.HasExited)
            throw new Exception(
                $"DAP.exe exited before bubble '{expectedInstruction}' was observed. ExitCode={dapProcess.ExitCode}.");

        AutomationElement? bubble = null;
        try
        {
            // The learner bubble is a top-level WPF Window. Searching the entire
            // desktop descendant tree can block for several seconds on unrelated UIA
            // providers and consume the whole E2E timeout before we inspect the bubble.
            // Restrict discovery to top-level windows and match either its stable
            // AutomationId or the current instruction exposed as the accessible Name.
            var topLevelWindows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
                .Cast<AutomationElement>();

            bubble = topLevelWindows
                .FirstOrDefault(candidate =>
                {
                    try
                    {
                        return IsVisibleUiaElement(candidate)
                               && (string.Equals(
                                       candidate.Current.AutomationId,
                                       "DapLearnerBubble",
                                       StringComparison.Ordinal)
                                   || string.Equals(
                                       candidate.Current.Name,
                                       expectedInstruction,
                                       StringComparison.Ordinal));
                    }
                    catch (ElementNotAvailableException)
                    {
                        return false;
                    }
                });

            if (bubble is null && !diagnosticLogged && sw.ElapsedMilliseconds >= 1_000)
            {
                diagnosticLogged = true;
                DiagnoseDapTopLevelWindows(dapProcess);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        if (bubble is not null)
        {
            try
            {
                lastObservedInstruction = bubble.Current.Name;
                if (string.Equals(lastObservedInstruction, expectedInstruction, StringComparison.Ordinal))
                    return bubble;
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        Thread.Sleep(100);
    }

    throw new TimeoutException(
        $"Timed out waiting for DAP Windows bubble '{expectedInstruction}'. " +
        $"Last observed bubble: '{lastObservedInstruction ?? "<none>"}'.");
}

bool IsVisibleUiaElement(AutomationElement candidate)
{
    try
    {
        return !candidate.Current.IsOffscreen
               && !candidate.Current.BoundingRectangle.IsEmpty;
    }
    catch (ElementNotAvailableException)
    {
        return false;
    }
}

void DiagnoseDapTopLevelWindows(Process dapProcess)
{
    try
    {
        var windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
            .Cast<AutomationElement>()
            .ToArray();

        Console.WriteLine($"[Windows UIA diagnostic] top-level windows={windows.Length}; launcher pid={dapProcess.Id}");

        foreach (var window in windows)
        {
            try
            {
                Console.WriteLine(
                    $"[Windows UIA diagnostic] top-level window: " +
                    $"ProcessId={window.Current.ProcessId}; " +
                    $"Name='{window.Current.Name}'; " +
                    $"AutomationId='{window.Current.AutomationId}'; " +
                    $"ClassName='{window.Current.ClassName}'; " +
                    $"IsOffscreen={window.Current.IsOffscreen}; " +
                    $"Bounds='{window.Current.BoundingRectangle}'");
            }
            catch (ElementNotAvailableException)
            {
            }
        }
    }
    catch (ElementNotAvailableException)
    {
        Console.WriteLine("[Windows UIA diagnostic] DAP top-level windows became unavailable.");
    }
}

async Task WaitForHttpAsync(string url, Process process, string processName)
{
    using var http = new HttpClient();
    var deadline = DateTime.UtcNow.AddSeconds(30);

    while (DateTime.UtcNow < deadline)
    {
        if (process.HasExited)
            throw new Exception($"{processName} exited before becoming ready. ExitCode={process.ExitCode}.");

        try
        {
            using var response = await http.GetAsync(url);
            if ((int)response.StatusCode < 500)
                return;
        }
        catch (HttpRequestException)
        {
        }

        await Task.Delay(200);
    }

    throw new TimeoutException($"{processName} did not become ready at {url} within 30 seconds.");
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
    catch (InvalidOperationException)
    {
    }
    catch (System.ComponentModel.Win32Exception)
    {
    }
}

void StopOwnedProcessTree(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000) && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000) && !process.HasExited)
                    throw new InvalidOperationException(
                        $"E2E-owned process {process.ProcessName} ({process.Id}) did not terminate during cleanup.");
            }
        }
    }
    catch (InvalidOperationException)
    {
    }
    catch (System.ComponentModel.Win32Exception)
    {
    }
    finally
    {
        process.Dispose();
    }
}

sealed class ManualHandoffCompleteException : Exception
{
}
