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

var unguided = args.Contains("--unguided", StringComparer.OrdinalIgnoreCase);
var guided = args.Contains("--guided", StringComparer.OrdinalIgnoreCase);

if (unguided && guided)
    throw new ArgumentException("--guided and --unguided cannot be combined.");

if (unguided)
{
    await RunPersistedUnguidedAsync();
    return;
}

if (guided)
{
    await RunGuidedAsync();
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
    Console.WriteLine("PASS: Windows CRM-only canonical 53-step Customer -> Site -> Case -> Lead scenario completed.");
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

        WaitForElementById(window, "NewCaseButton", 5_000);
        Console.WriteLine(
            $"PASS: Windows unguided executed {persistedSteps.Count} persisted Guide Steps from DAP.db without DAP.exe or bubbles.");
    }
    finally
    {
        if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
        if (backend is not null) StopOwnedProcessTree(backend);
    }
}

async Task RunGuidedAsync()
{
    var databaseOptions = SqliteDatabaseOptions.CreateDefault();
    var factory = new SqliteConnectionFactory(databaseOptions);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);
    var persistedSteps = await repository.GetStepsAsync(DapTestCrmWindowsGuideSeed.GuideId);
    if (persistedSteps.Count != 11)
    {
        throw new InvalidOperationException(
            $"Guide '{DapTestCrmWindowsGuideSeed.GuideId}' must contain exactly 11 persisted Steps for this milestone. " +
            "Run this project once with --reset-guide first.");
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
                Console.Error.WriteLine($"[DAP ERROR] {e.Data}");
        };
        dap.BeginOutputReadLine();
        dap.BeginErrorReadLine();

        var driver = new WindowsCrmScenarioDriver(windowsApp, window);

        string BubbleFor(string stepId) =>
            persistedSteps.Single(step => step.Id == stepId).Bubble.Content;

        WaitForBubble(BubbleFor("testcrm-windows-customer-name"), dap);
        await driver.SetCustomerSearch("אלפא פתרונות בע\"מ");

        WaitForBubble(BubbleFor("testcrm-windows-customer-search-button"), dap);
        await driver.SubmitCustomerSearch();

        WaitForBubble(BubbleFor("testcrm-windows-customer-result"), dap);
        await driver.OpenFirstCustomer();
        DiagnoseNavigationGrids(window);

        WaitForBubble(BubbleFor("testcrm-windows-site-row"), dap);
        await driver.OpenFirstSite();

        WaitForBubble(BubbleFor("testcrm-windows-cases-tab"), dap);
        await driver.OpenCases();

        WaitForBubble(BubbleFor("testcrm-windows-sort-cases"), dap);
        await driver.SortCasesByStatus();

        WaitForBubble(BubbleFor("testcrm-windows-new-case"), dap);
        await driver.CreateCase();

        WaitForBubble(BubbleFor("testcrm-windows-case-subject"), dap);
        await driver.SetCaseSubject("תקלה בחיבור לאינטרנט");

        WaitForBubble(BubbleFor("testcrm-windows-case-description"), dap);
        await driver.SetCaseDescription("הלקוח מדווח על חיבור לא יציב.");

        WaitForBubble(BubbleFor("testcrm-windows-save-new-case"), dap);
        await driver.SaveCase();
        DiagnoseBreadcrumbs(window);

        WaitForBubble(BubbleFor("testcrm-windows-back-to-cases"), dap);
        await driver.OpenSiteFromBreadcrumb();

        if (!dap.WaitForExit(15_000))
            throw new TimeoutException("DAP.exe did not complete after the validating Step 11 action.");
        if (dap.ExitCode != 0)
            throw new Exception($"DAP.exe exited with code {dap.ExitCode}.");

        WaitForElementById(window, "NewCaseButton", 5_000);
        Console.WriteLine("PASS: DAP Windows Learner Runtime persisted Steps 1 -> 11 with real UIA targets and bubbles.");
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

    while (sw.ElapsedMilliseconds < timeout)
    {
        if (dapProcess.HasExited)
            throw new Exception(
                $"DAP.exe exited before bubble '{expectedInstruction}' was observed. ExitCode={dapProcess.ExitCode}.");

        var bubble = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "DapLearnerBubble"));

        if (bubble is not null)
        {
            lastObservedInstruction = bubble.Current.Name;
            if (string.Equals(lastObservedInstruction, expectedInstruction, StringComparison.Ordinal))
                return bubble;
        }

        Thread.Sleep(100);
    }

    throw new TimeoutException(
        $"Timed out waiting for DAP Windows bubble '{expectedInstruction}'. " +
        $"Last observed bubble: '{lastObservedInstruction ?? "<none>"}'.");
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
