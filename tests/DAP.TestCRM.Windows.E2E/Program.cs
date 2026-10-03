using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Windows.Automation;
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

if (args.Contains("--reset-guide", StringComparer.OrdinalIgnoreCase))
{
    var options = SqliteDatabaseOptions.CreateDefault();
    var factory = new SqliteConnectionFactory(options);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);
    foreach (var step in DapTestCrmWindowsGuideSeed.CreateSteps())
        await repository.SaveStepAsync(DapTestCrmWindowsGuideSeed.GuideId, step);

    await repository.RenameGuideAsync(
        DapTestCrmWindowsGuideSeed.GuideId,
        DapTestCrmWindowsGuideSeed.GuideId,
        DapTestCrmWindowsGuideSeed.GuideName);

    Console.WriteLine(
        $"Reset Guide '{DapTestCrmWindowsGuideSeed.GuideId}' " +
        $"({DapTestCrmWindowsGuideSeed.CreateSteps().Count} steps) in {options.DatabasePath}");
    return;
}

int? manualFromStep = null;
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].Equals("--manual-from-step", StringComparison.OrdinalIgnoreCase))
        continue;

    if (i + 1 >= args.Length
        || !int.TryParse(args[++i], out var parsedManualStep)
        || parsedManualStep < 1)
        throw new ArgumentException("--manual-from-step requires a positive Guide Step order.");

    manualFromStep = parsedManualStep;
}

var unguided = args.Contains("--unguided", StringComparer.OrdinalIgnoreCase);
var guided = args.Contains("--guided", StringComparer.OrdinalIgnoreCase);

if (unguided && guided)
    throw new ArgumentException("--guided and --unguided cannot be combined.");
if (unguided && manualFromStep is not null)
    throw new ArgumentException("--unguided and --manual-from-step cannot be combined.");

if (unguided)
{
    await RunPersistedUnguidedAsync();
    return;
}

if (guided || manualFromStep is not null)
{
    await RunGuidedAsync(manualFromStep);
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

        backend = StartProcess(
            "dotnet",
            $"run --project \"{backendProject}\" --no-launch-profile",
            new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://localhost:5201" });

        await WaitForHttpAsync("http://localhost:5201/api/customers", backend, "TestCRM backend");

        windowsApp = StartProcess(
            "dotnet",
            $"run --project \"{appProject}\" --no-launch-profile");

        var window = WaitForMainWindow();
        var executor = new PersistedWindowsCrmGuideExecutor(
            new WindowsCrmScenarioDriver(windowsApp, window));

        await executor.RunAsync(persistedSteps);

        Console.WriteLine(
            $"PASS: Windows unguided executed {persistedSteps.Count} persisted Guide Steps from DAP.db without DAP.exe or bubbles.");
    }
    finally
    {
        if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
        if (backend is not null) StopOwnedProcessTree(backend);
    }
}

async Task RunGuidedAsync(int? handoffStepOrder = null)
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

    Process? backend = null;
    Process? windowsApp = null;
    Process? dap = null;

    try
    {
        EnsurePortFree(5201);

        backend = StartProcess(
            "dotnet",
            $"run --project \"{backendProject}\" --no-launch-profile",
            new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://localhost:5201" });

        await WaitForHttpAsync("http://localhost:5201/api/customers", backend, "TestCRM backend");

        windowsApp = StartProcess(
            "dotnet",
            $"run --project \"{appProject}\" --no-launch-profile");

        var window = WaitForMainWindow();
        var customerName = WaitForElementById(window, "CustomerNameSearch");

        dap = StartProcess(
            "dotnet",
            $"run --project \"{dapProject}\" --no-launch-profile --no-build -- " +
            $"--learner-windows {DapTestCrmWindowsGuideSeed.GuideId} " +
            $"--window-automation-id {mainWindowAutomationId}",
            redirectOutput: true);

        dap.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Console.WriteLine($"[DAP] {e.Data}");
        };
        dap.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Console.Error.WriteLine($"[DAP STDERR] {e.Data}");
        };
        dap.BeginOutputReadLine();
        dap.BeginErrorReadLine();

        var driver = new WindowsCrmScenarioDriver(windowsApp, window);

        string BubbleFor(string stepId) =>
            persistedSteps.Single(step => step.Id == stepId).Bubble.Content;

        void WaitForStep(string stepId)
        {
            var step = persistedSteps.Single(candidate => candidate.Id == stepId);
            WaitForBubble(step.Bubble.Content, dap);

            if (handoffStepOrder != step.Order)
                return;

            Console.WriteLine();
            Console.WriteLine($"MANUAL HANDOFF: Windows Step {step.Order}/{persistedSteps.Count} is ready.");
            Console.WriteLine("Automatic learner actions are paused. Continue manually in TestCRM by following the DAP bubbles.");
            Console.WriteLine("Press ENTER here only when you are finished with the manual run.");
            Console.ReadLine();

            throw new ManualHandoffCompleteException();
        }

        try
        {

        WaitForStep("testcrm-windows-customer-name");
        await driver.SetCustomerSearch("אלפא פתרונות בע\"מ");

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

        WaitForStep("testcrm-windows-back-to-cases");
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

        WaitForStep("testcrm-windows-delete-case");
        await driver.DeleteCase();

        WaitForStep("testcrm-windows-confirm-delete-case");
        await driver.ConfirmDelete();

        WaitForStep("testcrm-windows-header-home");
        await driver.GoPortal();

        if (!dap.WaitForExit(5_000))
            throw new TimeoutException("DAP.exe did not complete after the final Step 53 action.");
        if (dap.ExitCode != 0)
            throw new Exception($"DAP.exe exited with code {dap.ExitCode}.");

        Console.WriteLine("PASS: DAP Windows Learner Runtime completed all 53 persisted Guide Steps with real UIA targets, runtime capture, modal targeting, and bubbles.");
        }
        catch (ManualHandoffCompleteException)
        {
            Console.WriteLine("Windows manual learner run finished by operator request.");
        }
    }
    finally
    {
        if (dap is not null) StopOwnedProcessTree(dap);
        if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
        if (backend is not null) StopOwnedProcessTree(backend);
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
    bool redirectOutput = false)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = root,
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

void StopOwnedProcessTree(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
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
