using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
    foreach (var step in DapTestCrmWindowsGuideSeed.CreateFirstTwoSteps())
        await repository.SaveStepAsync(DapTestCrmWindowsGuideSeed.GuideId, step);

    await repository.RenameGuideAsync(
        DapTestCrmWindowsGuideSeed.GuideId,
        DapTestCrmWindowsGuideSeed.GuideId,
        DapTestCrmWindowsGuideSeed.GuideName);

    Console.WriteLine(
        $"Reset Guide '{DapTestCrmWindowsGuideSeed.GuideId}' " +
        $"({DapTestCrmWindowsGuideSeed.CreateFirstTwoSteps().Count} steps) in {options.DatabasePath}");
    return;
}

if (args.Contains("--dap-first-two", StringComparer.OrdinalIgnoreCase))
{
    await RunDapFirstTwoAsync();
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

async Task RunDapFirstTwoAsync()
{
    var databaseOptions = SqliteDatabaseOptions.CreateDefault();
    var factory = new SqliteConnectionFactory(databaseOptions);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);
    var persistedSteps = await repository.GetStepsAsync(DapTestCrmWindowsGuideSeed.GuideId);
    if (persistedSteps.Count != 2)
    {
        throw new InvalidOperationException(
            $"Guide '{DapTestCrmWindowsGuideSeed.GuideId}' must contain exactly 2 persisted Steps for this milestone. " +
            "Run this project once with --reset-guide first.");
    }

    Process? backend = null;
    Process? windowsApp = null;
    Process? dap = null;

    try
    {
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

        var firstBubble = WaitForBubble("חפש את הלקוח: אלפא פתרונות בע\"מ", dap);
        Console.WriteLine($"DAP Windows Step 1 bubble: {firstBubble.Current.Name}");

        if (!customerName.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
            throw new Exception("CustomerNameSearch does not expose ValuePattern.");
        ((ValuePattern)valuePattern).SetValue("אלפא פתרונות בע\"מ");

        var secondBubble = WaitForBubble("לחץ על חיפוש", dap);
        Console.WriteLine($"DAP Windows Step 2 bubble: {secondBubble.Current.Name}");

        var searchButton = WaitForElementById(window, "SearchCustomersButton");
        if (!searchButton.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern))
            throw new Exception("SearchCustomersButton does not expose InvokePattern.");
        ((InvokePattern)invokePattern).Invoke();

        if (!dap.WaitForExit(15_000))
            throw new TimeoutException("DAP.exe did not complete after the validating Step 2 click.");
        if (dap.ExitCode != 0)
            throw new Exception($"DAP.exe exited with code {dap.ExitCode}.");

        WaitForElementById(window, "CustomersGrid");
        Console.WriteLine("PASS: DAP Windows Learner Runtime persisted Step 1 -> Step 2 with real UIA targets and bubbles.");
    }
    finally
    {
        if (dap is not null) StopOwnedProcessTree(dap);
        if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
        if (backend is not null) StopOwnedProcessTree(backend);
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

AutomationElement WaitForBubble(string expectedInstruction, Process dapProcess, int timeout = 30_000)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeout)
    {
        if (dapProcess.HasExited)
            throw new Exception(
                $"DAP.exe exited before bubble '{expectedInstruction}' was observed. ExitCode={dapProcess.ExitCode}.");

        var bubble = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "DapLearnerBubble"));

        if (bubble is not null
            && string.Equals(bubble.Current.Name, expectedInstruction, StringComparison.Ordinal))
            return bubble;

        Thread.Sleep(100);
    }

    throw new TimeoutException($"Timed out waiting for DAP Windows bubble '{expectedInstruction}'.");
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
