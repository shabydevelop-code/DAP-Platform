using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
        if (i + 1 >= args.Length
            || !int.TryParse(args[++i], out var parsedManualStep)
            || parsedManualStep < 1)
            throw new ArgumentException("--manual-from-step requires a positive Guide Step order.");

        manualFromStep = parsedManualStep;
        continue;
    }

    if (args[i].Equals("--fast-from-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length
            || !int.TryParse(args[++i], out var parsedFastStep)
            || parsedFastStep < 1)
            throw new ArgumentException("--fast-from-step requires a positive Guide Step order.");

        fastFromStep = parsedFastStep;
        continue;
    }

    if (args[i].Equals("--visual-from-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length
            || !int.TryParse(args[++i], out var parsedVisualStep)
            || parsedVisualStep < 1)
            throw new ArgumentException("--visual-from-step requires a positive Guide Step order.");

        visualFromStep = parsedVisualStep;
    }
}

var focusedModeCount = new[] { manualFromStep, fastFromStep, visualFromStep }.Count(step => step is not null);
if (focusedModeCount > 1)
    throw new ArgumentException("--manual-from-step, --fast-from-step, and --visual-from-step cannot be combined.");

var unguided = args.Contains("--unguided", StringComparer.OrdinalIgnoreCase);
var guided = args.Contains("--guided", StringComparer.OrdinalIgnoreCase);
var manual = args.Contains("--manual", StringComparer.OrdinalIgnoreCase);
var explicitFast = args.Contains("--fast", StringComparer.OrdinalIgnoreCase);
var explicitVisual = args.Contains("--visual", StringComparer.OrdinalIgnoreCase);

if (explicitFast && explicitVisual)
    throw new ArgumentException("--fast and --visual cannot be combined.");
if ((explicitFast || explicitVisual) && !guided)
    throw new ArgumentException("--fast and --visual require --guided.");
if (unguided && guided)
    throw new ArgumentException("--guided and --unguided cannot be combined.");
if (manual && (unguided || guided || manualFromStep is not null || fastFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--manual cannot be combined with --guided, --unguided, or a from-step mode.");
if (unguided && (manualFromStep is not null || fastFromStep is not null || visualFromStep is not null))
    throw new ArgumentException("--unguided cannot be combined with a from-step mode.");

if (unguided)
{
    await RunPersistedUnguidedAsync();
    return;
}

if (manual)
{
    await RunGuidedAsync(handoffStepOrder: 1, fullManual: true);
    return;
}

if (guided || manualFromStep is not null || fastFromStep is not null || visualFromStep is not null)
{
    await RunGuidedAsync(
        handoffStepOrder: manualFromStep,
        fastStartStepOrder: fastFromStep,
        visualStartStepOrder: visualFromStep,
        visualFromStart: guided && explicitVisual);
    return;
}

using var app = Process.Start(new ProcessStartInfo(
    "dotnet",
    $"run --project \"{appProject}\" --no-launch-profile")
{
    WorkingDirectory = root,
    UseShellExecute = false
}) ?? throw new Exception("Could not start Windows TestCRM.");

try
{
    var window = WaitForMainWindow();
    await CanonicalCrmScenario.Run53Async(new WindowsCrmScenarioDriver(app, window));
    Console.WriteLine("PASS: Windows canonical 53-step Customer -> Site -> Case -> Lead scenario completed.");
}
catch (TargetApplicationClosedException)
{
    Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
}
catch (Exception) when (app.HasExited)
{
    Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
}
finally
{
    StopOwnedProcessTree(app);
}

async Task RunPersistedUnguidedAsync()
{
    var databaseOptions = SqliteDatabaseOptions.CreateDefault();
    var factory = new SqliteConnectionFactory(databaseOptions);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);
    var persistedSteps = await repository.GetStepsAsync(DapTestCrmWindowsGuideSeed.GuideId);

    Process? backend = null;
    Process? windowsApp = null;

    try
    {
        EnsurePortFree(5201);

        backend = packagedDiagnostics
            ? StartProcess(
                "dotnet",
                $"\"{Path.Combine(packagedServerDirectory!, "DAP.TestCRM.Server.dll")}\"",
                new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://localhost:5201" },
                workingDirectory: packagedServerDirectory)
            : StartProcess(
                "dotnet",
                $"run --project \"{backendProject}\" --no-launch-profile",
                new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://localhost:5201" });

        await WaitForHttpAsync("http://localhost:5201/api/customers", backend, "TestCRM backend");

        windowsApp = packagedDiagnostics
            ? StartProcess(
                Path.Combine(packagedWindowsDirectory!, "DAP.TestCRM.Windows.exe"),
                string.Empty,
                workingDirectory: packagedWindowsDirectory)
            : StartProcess(
                "dotnet",
                $"run --project \"{appProject}\" --no-launch-profile");

        var window = WaitForMainWindow();
        var executor = new PersistedWindowsCrmGuideExecutor(
            new WindowsCrmScenarioDriver(windowsApp, window));

        Console.WriteLine("E2E mode: unguided");
        await executor.RunAsync(persistedSteps);

        Console.WriteLine(
            $"PASS: Windows unguided executed {persistedSteps.Count} persisted Guide Steps from DAP.db without DAP.exe or bubbles.");
    }
    catch (TargetApplicationClosedException)
    {
        Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
    }
    catch (Exception) when (windowsApp is not null && windowsApp.HasExited)
    {
        Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
    }
    finally
    {
        if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
        if (backend is not null) StopOwnedProcessTree(backend);
    }
}

async Task RunGuidedAsync(
    int? handoffStepOrder = null,
    int? visualStartStepOrder = null,
    bool visualFromStart = false,
    bool fullManual = false)
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

    if (handoffStepOrder is not null
        && !persistedSteps.Any(step => step.Order == handoffStepOrder.Value))
    {
        throw new ArgumentOutOfRangeException(
            nameof(handoffStepOrder),
            handoffStepOrder,
            $"Guide '{DapTestCrmWindowsGuideSeed.GuideId}' does not contain Step {handoffStepOrder}.");
    }

    if (visualStartStepOrder is not null
        && !persistedSteps.Any(step => step.Order == visualStartStepOrder.Value))
    {
        throw new ArgumentOutOfRangeException(
            nameof(visualStartStepOrder),
            visualStartStepOrder,
            $"Guide '{DapTestCrmWindowsGuideSeed.GuideId}' does not contain Step {visualStartStepOrder}.");
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

        var focusedStartStepOrder = fullManual ? null : handoffStepOrder ?? visualStartStepOrder;
        var bootstrapCaptures = new Dictionary<string, string>(StringComparer.Ordinal);
        var resumeContextPath = Path.Combine(runRoot, "resume-context.json");

        Process StartDap(int? startStepOrder)
        {
            var startStepArgument = startStepOrder is not null
                ? $" --start-step {startStepOrder.Value}"
                : string.Empty;
            var resumeContextArgument = string.Empty;

            if (bootstrapCaptures.Count > 0)
            {
                Directory.CreateDirectory(runRoot);
                File.WriteAllText(
                    resumeContextPath,
                    JsonSerializer.Serialize(bootstrapCaptures));
                resumeContextArgument = $" --resume-context-file \"{resumeContextPath}\"";
            }

            var process = StartProcess(
                dapExe,
                $"--learner-windows {DapTestCrmWindowsGuideSeed.GuideId} " +
                $"--window-automation-id {mainWindowAutomationId}" +
                startStepArgument +
                resumeContextArgument,
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

        if (focusedStartStepOrder is null)
            dap = StartDap(null);

        var driver = new WindowsCrmScenarioDriver(windowsApp, window, visualFromStart);

        Console.WriteLine(
            fullManual
                ? "E2E mode: manual"
                : handoffStepOrder is not null
                    ? $"E2E mode: unguided -> manual from Step {handoffStepOrder}"
                    : visualStartStepOrder is not null
                    ? $"E2E mode: unguided -> visual from Step {visualStartStepOrder}"
                    : $"E2E mode: {(visualFromStart ? "visual" : "fast")}");

        void WaitForStep(string stepId)
        {
            var step = persistedSteps.Single(candidate => candidate.Id == stepId);

            if (focusedStartStepOrder is not null && step.Order < focusedStartStepOrder.Value)
            {
                if (step.Capture is not null)
                {
                    var captured = step.Id == "testcrm-windows-back-to-cases"
                        ? driver.CreatedCaseId
                        : null;
                    if (string.IsNullOrWhiteSpace(captured))
                        throw new InvalidOperationException(
                            $"Windows bootstrap could not capture runtime value for Step {step.Order} '{step.Id}'.");

                    bootstrapCaptures[step.Id] = captured;
                    Console.WriteLine($"Windows unguided bootstrap captured Step {step.Order}: {step.Id}");
                }
                else
                {
                    Console.WriteLine($"Windows unguided bootstrap Step {step.Order}/{persistedSteps.Count}: {step.Id}");
                }
                return;
            }

            if (dap is null)
            {
                if (focusedStartStepOrder != step.Order)
                    throw new InvalidOperationException(
                        $"Windows DAP launch expected at Step {focusedStartStepOrder}, but scenario reached Step {step.Order}.");

                dap = StartDap(step.Order);
                Console.WriteLine(
                    $"Windows unguided bootstrap complete through Step {step.Order - 1}; DAP started at Step {step.Order} with {bootstrapCaptures.Count} resume capture(s).");
            }

            WaitForBubble(step.Bubble.Content, dap!);

            if (visualStartStepOrder == step.Order && !driver.VisualMode)
            {
                driver.SetVisualMode(true);
                Console.WriteLine($"E2E mode transition: UNGUIDED -> VISUAL at Step {step.Order}");
            }

            driver.VisualPause(500);

            if (handoffStepOrder != step.Order)
                return;

            Console.WriteLine();
            Console.WriteLine(
                handoffStepOrder == 1
                    ? $"MANUAL WINDOWS RUN: Step {step.Order}/{persistedSteps.Count} is ready."
                    : $"MANUAL HANDOFF: Windows Step {step.Order}/{persistedSteps.Count} is ready.");
            Console.WriteLine("Automatic learner actions are paused. Continue manually in TestCRM by following the DAP bubbles.");
            Console.WriteLine("The run will close automatically when DAP completes the Guide or the Windows target application is closed.");
            Console.WriteLine("Press ENTER only if you want to stop the manual run before Guide completion.");

            while (!dap!.HasExited && !windowsApp.HasExited)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
                    break;

                Thread.Sleep(100);
            }

            if (dap!.HasExited)
            {
                if (dap.ExitCode != 0)
                    throw new Exception($"DAP.exe exited with code {dap.ExitCode} during the manual learner run.");

                Console.WriteLine("DAP completed the manual Guide. Closing the E2E-owned processes.");
            }
            else if (windowsApp.HasExited)
            {
                Console.WriteLine("Windows target application closed. Ending the manual learner run and cleaning up owned processes.");
            }

            throw new ManualHandoffCompleteException();
        }

        try
        {

        WaitForStep("testcrm-windows-customer-name");

        if (focusedStartStepOrder is not null)
        {
            // Focused runs use Steps before N only to establish real business
            // state; do not run the Step-1 regression sequence during bootstrap.
            await driver.SetCustomerSearch("אלפא פתרונות בע\"מ");
        }
        else
        {
            // Regression guard: an invalid committed value must not make the Step
            // permanently "armed". After the invalid blur, completing the exact
            // value while focus remains in the editor must still keep Step 1 active.
            await driver.SetCustomerSearch("אלפא");
            WaitForStep("testcrm-windows-customer-name");

            await driver.SetCustomerSearchWithoutCommit("אלפא פתרונות בע\"מ");
            Thread.Sleep(350);
            WaitForStep("testcrm-windows-customer-name");
            Console.WriteLine("DAP Windows text validation waits for a new blur after an invalid commit: PASS");

            await driver.CommitCustomerSearchEdit();
        }

        WaitForStep("testcrm-windows-customer-search-button");
        await driver.SubmitCustomerSearch();

        WaitForStep("testcrm-windows-customer-result");
        await driver.OpenFirstCustomer();
        DiagnoseNavigationGrids(window);

        WaitForStep("testcrm-windows-site-row");
        await driver.OpenFirstSite();

        WaitForStep("testcrm-windows-cases-tab");
        await driver.OpenCases();

        WaitForStep("testcrm-windows-sort-cases");
        await driver.SortCasesByStatus();

        WaitForStep("testcrm-windows-new-case");
        await driver.CreateCase();

        WaitForStep("testcrm-windows-case-subject");
        await driver.SetCaseSubject("תקלה בחיבור לאינטרנט");

        WaitForStep("testcrm-windows-case-description");
        await driver.SetCaseDescription("הלקוח מדווח על חיבור לא יציב.");

        WaitForStep("testcrm-windows-save-new-case");
        await driver.SaveCase();
        DiagnoseBreadcrumbs(window);

        // Focused bootstrap runs before DAP starts, so capture the business value
        // needed for resume-context directly from the real CRM state. In a normal
        // learner run, capture only after Runtime has advanced to the next Step.
        var backToCasesStep = persistedSteps.Single(step => step.Id == "testcrm-windows-back-to-cases");
        if (focusedStartStepOrder is not null && backToCasesStep.Order < focusedStartStepOrder.Value)
            driver.CaptureCreatedCaseId();

        WaitForStep("testcrm-windows-back-to-cases");

        if (focusedStartStepOrder is null || backToCasesStep.Order >= focusedStartStepOrder.Value)
            driver.CaptureCreatedCaseId();

        await driver.OpenSiteFromBreadcrumb();

        try
        {
            WaitForStep("testcrm-windows-open-created-case");
        }
        catch (TimeoutException)
        {
            DiagnoseCasesGrid(window);
            throw;
        }
        await driver.OpenCreatedCase();

        WaitForStep("testcrm-windows-case-in-progress");
        await driver.SetCaseStatus("בטיפול");

        WaitForStep("testcrm-windows-resolution-notes");
        await driver.SetResolutionNotes("נבדקה תשתית הלקוח");

        WaitForStep("testcrm-windows-activity-more");
        await driver.ShowMoreActivity();

        WaitForStep("testcrm-windows-case-closed");
        await driver.SetCaseStatus("סגורה");

        WaitForStep("testcrm-windows-case-subject-after-close");
        await driver.SetCaseSubject("תקלה בחיבור לאינטרנט");

        WaitForStep("testcrm-windows-attempt-close-save");
        await driver.SaveCase();

        WaitForStep("testcrm-windows-confirm-close-validation");
        await driver.DismissValidation();

        WaitForStep("testcrm-windows-close-reason");
        await driver.SetCloseReason("טופל");

        WaitForStep("testcrm-windows-save-closed-case");
        await driver.SaveCase();

        WaitForStep("testcrm-windows-return-site");
        await driver.OpenSiteFromBreadcrumb();

        WaitForStep("testcrm-windows-open-leads-tab");
        await driver.OpenLeads();

        WaitForStep("testcrm-windows-return-cases-tab");
        await driver.OpenCases();

        WaitForStep("testcrm-windows-open-leads-again");
        await driver.OpenLeads();

        WaitForStep("testcrm-windows-new-lead");
        await driver.CreateLead();

        WaitForStep("testcrm-windows-lead-contact");
        await driver.SetLeadContact("דנה כהן");

        WaitForStep("testcrm-windows-save-new-lead");
        await driver.SaveLead();

        WaitForStep("testcrm-windows-lead-close-success-1");
        await driver.SetLeadStatus("נסגר בהצלחה");

        WaitForStep("testcrm-windows-lead-new");
        await driver.SetLeadStatus("חדש");

        WaitForStep("testcrm-windows-lead-close-success-2");
        await driver.SetLeadStatus("נסגר בהצלחה");

        WaitForStep("testcrm-windows-lead-invalid-save");
        await driver.SaveLead();

        WaitForStep("testcrm-windows-lead-validation-ok");
        await driver.DismissValidation();

        WaitForStep("testcrm-windows-lead-service");
        await driver.SetLeadService("תמיכה מורחבת");

        WaitForStep("testcrm-windows-save-lead");
        await driver.SaveLead();

        WaitForStep("testcrm-windows-delete-lead");
        await driver.DeleteLead();

        WaitForStep("testcrm-windows-confirm-delete-lead");
        await driver.ConfirmDelete();

        WaitForStep("testcrm-windows-leads-to-customer");
        await driver.OpenCustomerFromBreadcrumb();

        WaitForStep("testcrm-windows-customer-site");
        await driver.OpenFirstSite();

        WaitForStep("testcrm-windows-site-leads");
        await driver.OpenLeads();

        WaitForStep("testcrm-windows-open-lead");
        await driver.OpenLeadByContactName("אבי כהן");

        WaitForStep("testcrm-windows-layout-status-new");
        await driver.SetLeadStatus("חדש");

        WaitForStep("testcrm-windows-layout-status-closed");
        await driver.SetLeadStatus("נסגר בהצלחה");

        WaitForStep("testcrm-windows-race-status-new");
        await driver.SetLeadStatus("חדש");

        WaitForStep("testcrm-windows-race-status-closed");
        await driver.SetLeadStatus("נסגר בהצלחה");

        WaitForStep("testcrm-windows-lead-to-site");
        await driver.OpenSiteFromBreadcrumb();

        WaitForStep("testcrm-windows-site-cases-final");
        await driver.OpenCases();

        WaitForStep("testcrm-windows-open-context-case");
        await driver.OpenCreatedCase();

        WaitForStep("testcrm-windows-context-back-site");
        await driver.OpenSiteFromBreadcrumb();

        WaitForStep("testcrm-windows-open-created-case-final");
        await driver.OpenCreatedCase();

        WaitForStep("testcrm-windows-before-delete-case-info");
        if (dap is not null)
            ClickCenteredInformationConfirm(driver);

        WaitForStep("testcrm-windows-delete-case");
        await driver.DeleteCase();

        WaitForStep("testcrm-windows-confirm-delete-case");
        await driver.ConfirmDelete();

        WaitForStep("testcrm-windows-header-home");
        await driver.GoPortal();

        var completedDap = dap ?? throw new InvalidOperationException("DAP process is not available at Guide completion.");
        var completionBubble = WaitForCompletionBubble(completedDap);
        if (driver.VisualMode)
            Thread.Sleep(800);
        ClickCompletionFinish(completionBubble, driver);

        if (!completedDap.WaitForExit(5_000))
            throw new TimeoutException("DAP.exe did not complete after the completion Finish action.");
        if (completedDap.ExitCode != 0)
            throw new Exception($"DAP.exe exited with code {completedDap.ExitCode}.");

        Console.WriteLine($"PASS: DAP Windows Learner Runtime completed all {persistedSteps.Count} persisted Guide Steps with real UIA targets, runtime capture, modal targeting, centered information, and bubbles.");
        }
        catch (ManualHandoffCompleteException)
        {
            Console.WriteLine("Windows manual learner run finished by operator request.");
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

void DiagnoseNavigationGrids(AutomationElement window)
{
    foreach (var gridId in new[] { "CustomersGrid", "SitesGrid" })
    {
        try
        {
            var grid = window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, gridId));

            if (grid is null)
            {
                Console.WriteLine($"[Windows UIA diagnostic] {gridId}: <not found>");
                continue;
            }

            var rows = grid.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));

            Console.WriteLine(
                $"[Windows UIA diagnostic] {gridId}: " +
                $"IsOffscreen={grid.Current.IsOffscreen}; " +
                $"IsEnabled={grid.Current.IsEnabled}; " +
                $"rows={rows.Count}");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                Console.WriteLine(
                    $"[Windows UIA diagnostic] {gridId} row {i}: " +
                    $"AutomationId='{row.Current.AutomationId}'; " +
                    $"Name='{row.Current.Name}'; " +
                    $"IsOffscreen={row.Current.IsOffscreen}; " +
                    $"IsEnabled={row.Current.IsEnabled}");

                var ancestors = new List<string>();
                for (var current = TreeWalker.ControlViewWalker.GetParent(row);
                     current is not null && ancestors.Count < 6;
                     current = TreeWalker.ControlViewWalker.GetParent(current))
                {
                    ancestors.Add(
                        $"{current.Current.ControlType?.ProgrammaticName ?? "<null>"}" +
                        $"(AutomationId='{current.Current.AutomationId}',Name='{current.Current.Name}',IsOffscreen={current.Current.IsOffscreen})");
                }
                Console.WriteLine(
                    $"[Windows UIA diagnostic] {gridId} row {i} ControlView ancestors: " +
                    string.Join(" <- ", ancestors));
            }
        }
        catch (ElementNotAvailableException)
        {
            Console.WriteLine($"[Windows UIA diagnostic] {gridId}: <became unavailable>");
        }
    }
}

void DiagnoseCasesGrid(AutomationElement window)
{
    try
    {
        var grid = window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CasesGrid"));

        if (grid is null)
        {
            Console.WriteLine("[Windows UIA diagnostic] CasesGrid: <not found>");
            return;
        }

        var rows = grid.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem))
            .Cast<AutomationElement>()
            .ToArray();

        Console.WriteLine($"[Windows UIA diagnostic] CasesGrid rows={rows.Length}");
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            Console.WriteLine(
                $"[Windows UIA diagnostic] CasesGrid row {index}: " +
                $"AutomationId='{row.Current.AutomationId}'; Name='{row.Current.Name}'; " +
                $"ControlType='{row.Current.ControlType?.ProgrammaticName ?? "<null>"}'");

            var descendants = row.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .ToArray();

            foreach (var element in descendants)
            {
                Console.WriteLine(
                    $"[Windows UIA diagnostic] CasesGrid row {index} descendant: " +
                    $"AutomationId='{element.Current.AutomationId}'; Name='{element.Current.Name}'; " +
                    $"ControlType='{element.Current.ControlType?.ProgrammaticName ?? "<null>"}'");
            }
        }
    }
    catch (ElementNotAvailableException)
    {
        Console.WriteLine("[Windows UIA diagnostic] CasesGrid: <became unavailable>");
    }
}

void DiagnoseBreadcrumbs(AutomationElement window)
{
    try
    {
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

        var breadcrumbs = buttons.Cast<AutomationElement>()
            .Where(element =>
                string.Equals(element.Current.AutomationId, "Breadcrumb", StringComparison.Ordinal)
                || string.Equals(element.Current.Name, "מטה תל אביב", StringComparison.Ordinal))
            .ToArray();

        Console.WriteLine($"[Windows UIA diagnostic] breadcrumb candidates={breadcrumbs.Length}");
        foreach (var element in breadcrumbs)
        {
            Console.WriteLine(
                $"[Windows UIA diagnostic] breadcrumb: " +
                $"AutomationId='{element.Current.AutomationId}'; " +
                $"Name='{element.Current.Name}'; " +
                $"ControlType='{element.Current.ControlType?.ProgrammaticName ?? "<null>"}'; " +
                $"IsOffscreen={element.Current.IsOffscreen}; " +
                $"IsEnabled={element.Current.IsEnabled}; " +
                $"Bounds='{element.Current.BoundingRectangle}'");
        }
    }
    catch (ElementNotAvailableException)
    {
        Console.WriteLine("[Windows UIA diagnostic] breadcrumbs: <became unavailable>");
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

void ClickCenteredInformationConfirm(WindowsCrmScenarioDriver driver)
{
    var centered = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
        .Cast<AutomationElement>()
        .FirstOrDefault(candidate =>
        {
            try
            {
                return IsVisibleUiaElement(candidate)
                       && string.Equals(
                           candidate.Current.AutomationId,
                           "DapLearnerCenteredBubble",
                           StringComparison.Ordinal);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        })
        ?? throw new Exception("DAP Windows centered information bubble was not found.");

    var confirm = centered.FindFirst(
        TreeScope.Descendants,
        new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            "DapLearnerCenteredConfirm"));

    if (confirm is null)
        throw new Exception("DAP Windows centered information Confirm action was not found.");

    if (!confirm.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        throw new Exception("DAP Windows centered information Confirm action does not expose InvokePattern.");

    driver.VisualTarget(confirm);
    ((InvokePattern)invoke).Invoke();
}

AutomationElement WaitForCompletionBubble(Process dapProcess, int timeout = 5_000)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeout)
    {
        if (dapProcess.HasExited)
            throw new Exception(
                $"DAP.exe exited before the Windows completion bubble was observed. ExitCode={dapProcess.ExitCode}.");

        try
        {
            var topLevelWindows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
                .Cast<AutomationElement>();

            var completion = topLevelWindows.FirstOrDefault(candidate =>
            {
                try
                {
                    return IsVisibleUiaElement(candidate)
                           && string.Equals(
                               candidate.Current.AutomationId,
                               "DapLearnerCompletionBubble",
                               StringComparison.Ordinal);
                }
                catch (ElementNotAvailableException)
                {
                    return false;
                }
            });

            if (completion is not null)
                return completion;
        }
        catch (ElementNotAvailableException)
        {
        }

        Thread.Sleep(100);
    }

    throw new TimeoutException("Timed out waiting for DAP Windows completion bubble.");
}

void ClickCompletionFinish(AutomationElement completionBubble, WindowsCrmScenarioDriver driver)
{
    var finish = completionBubble.FindFirst(
        TreeScope.Descendants,
        new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            "DapLearnerCompletionFinish"));

    if (finish is null)
        throw new Exception("DAP Windows completion Finish action was not found.");

    if (!finish.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        throw new Exception("DAP Windows completion Finish action does not expose InvokePattern.");

    driver.VisualTarget(finish);
    ((InvokePattern)invoke).Invoke();
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
