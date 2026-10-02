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

        WaitForElementById(window, "DeleteCaseButton", 5_000);
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
    if (persistedSteps.Count != 10)
    {
        throw new InvalidOperationException(
            $"Guide '{DapTestCrmWindowsGuideSeed.GuideId}' must contain exactly 10 persisted Steps for this milestone. " +
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
            $"run --project \"{dapProject}\" --no-launch-profile -- " +
            $"--learner-windows {DapTestCrmWindowsGuideSeed.GuideId} " +
            $"--window-automation-id {mainWindowAutomationId}");

        var driver = new WindowsCrmScenarioDriver(windowsApp, window);

        WaitForBubble("חפש את הלקוח: אלפא פתרונות בע\"מ", dap);
        await driver.SetCustomerSearch("אלפא פתרונות בע\"מ");

        WaitForBubble("לחץ על חיפוש", dap);
        await driver.SubmitCustomerSearch();

        WaitForBubble("פתח את הלקוח מתוצאות החיפוש", dap);
        await driver.OpenFirstCustomer();

        WaitForBubble("פתח את האתר הראשון של הלקוח", dap);
        await driver.OpenFirstSite();

        WaitForBubble("עבור ללשונית פניות", dap);
        await driver.OpenCases();

        WaitForBubble("מיין את הפניות לפי סטטוס", dap);
        await driver.SortCasesByStatus();

        WaitForBubble("צור פנייה חדשה", dap);
        await driver.CreateCase();

        WaitForBubble("הקלד את נושא הפנייה", dap);
        await driver.SetCaseSubject("תקלה בחיבור לאינטרנט");

        WaitForBubble("תאר את הפנייה", dap);
        await driver.SetCaseDescription("הלקוח מדווח על חיבור לא יציב.");

        WaitForBubble("שמור את הפנייה החדשה", dap);
        await driver.SaveCase();

        if (!dap.WaitForExit(15_000))
            throw new TimeoutException("DAP.exe did not complete after the validating Step 10 action.");
        if (dap.ExitCode != 0)
            throw new Exception($"DAP.exe exited with code {dap.ExitCode}.");

        WaitForElementById(window, "DeleteCaseButton");
        Console.WriteLine("PASS: DAP Windows Learner Runtime persisted Steps 1 -> 10 with real UIA targets and bubbles.");
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
    IReadOnlyDictionary<string, string?>? environment = null)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = root,
        UseShellExecute = false
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
