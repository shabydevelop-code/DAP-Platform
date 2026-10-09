using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using DAP.Data.Sqlite;
using DAP.App.Localization;
using DAP.Core.Localization;
using DAP.Data.Sqlite.Guides;
using DAP.Runtime.Web.Browser;
using DAP.Runtime.Web.Learner;
using DAP.Runtime.Windows.Bubbles;
using DAP.Runtime.Windows.Learner;
using DAP.Runtime.Windows.Targets;

namespace DAP.App;

public static class DapApplicationHost
{
    private static void StartupMark(Stopwatch timer, string stage)
        => Console.Error.WriteLine($"[DAP startup] {timer.Elapsed.TotalMilliseconds:F0} ms - {stage}");

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var startup = Stopwatch.StartNew();
        StartupMark(startup, "host entered");

        IUiTextProvider texts = JsonUiTextProvider.LoadFromApplicationDirectory();
        StartupMark(startup, $"localization loaded ({texts.Language})");

        var options = DapLaunchOptions.Parse(args, texts);
        if (options is null)
            return 2;
        StartupMark(startup, "launch options parsed");

        // A production Hybrid executor must own actions inside the learner runtime.
        // Never silently run a Hybrid request as Manual or delegate it to TestCRM E2E.


        var databaseOptions = SqliteDatabaseOptions.CreateDefault();
        var connections = new SqliteConnectionFactory(databaseOptions);
        await new SqliteDatabaseInitializer(connections).InitializeAsync(cancellationToken);
        var repository = new SqliteGuideStepRepository(connections);
        StartupMark(startup, "SQLite initialized");

        if (options.Mode == DapLaunchMode.InfrastructureCheck)
        {
            var diagnosticsPath = Path.Combine(
                Path.GetTempPath(),
                "DAP",
                "dap-check.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);
            await File.WriteAllTextAsync(
                diagnosticsPath,
                $"DAP.exe ready.{Environment.NewLine}Database: {databaseOptions.DatabasePath}{Environment.NewLine}UTC: {DateTimeOffset.UtcNow:O}{Environment.NewLine}",
                cancellationToken);

            MessageBox.Show(
                texts.Format("App.InfrastructureReady", databaseOptions.DatabasePath, diagnosticsPath),
                texts.Get("App.InfrastructureCheckTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return 0;
        }

        var steps = await repository.GetStepsAsync(options.GuideId!, cancellationToken);
        var applicationContexts = await repository.GetApplicationContextsAsync(options.GuideId!, cancellationToken);
        StartupMark(startup, $"guide loaded ({steps.Count} steps, {applicationContexts.Count} application contexts)");
        if (steps.Count == 0)
        {
            MessageBox.Show(
                texts.Format("Learner.GuideHasNoSteps", options.GuideId!),
                texts.Get("Learner.WindowTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 5;
        }

        var resumeContext = await LoadResumeContextAsync(options.ResumeContextPath, cancellationToken);
        ValidateResumeContext(steps, options.StartStep, resumeContext);
        if (resumeContext.Count > 0)
            StartupMark(startup, $"resume context loaded ({resumeContext.Count} captures)");

        var enabledTargetRuntimes = steps
            .Where(step => step.IsEnabled && step.Target is not null)
            .Select(step => step.Target!.Runtime)
            .Distinct()
            .ToArray();

        if (enabledTargetRuntimes.Length != 1)
            throw new InvalidOperationException(
                $"Guide '{options.GuideId}' must currently contain enabled targets for exactly one Runtime; found {enabledTargetRuntimes.Length}.");

        return enabledTargetRuntimes[0] switch
        {
            DAP.Core.Targets.TargetRuntime.Windows => await RunWindowsAsync(
                options, steps, applicationContexts, resumeContext, texts, startup, cancellationToken),
            DAP.Core.Targets.TargetRuntime.Web => await RunWebAsync(
                options, steps, applicationContexts, resumeContext, texts, startup, cancellationToken),
            _ => throw new NotSupportedException(
                $"Guide '{options.GuideId}' uses unsupported Runtime '{enabledTargetRuntimes[0]}'.")
        };
    }

    private static void ValidateResumeContext(
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        int? startStep,
        IReadOnlyDictionary<string, string> resumeContext)
    {
        if (resumeContext.Count == 0)
            return;

        if (startStep is null)
            throw new InvalidOperationException(
                "A resume context requires --start-step so DAP has an explicit continuation point.");

        var byId = steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        foreach (var pair in resumeContext)
        {
            if (!byId.TryGetValue(pair.Key, out var sourceStep))
                throw new InvalidOperationException(
                    $"Resume context references unknown Guide Step '{pair.Key}'.");

            if (sourceStep.Capture is null)
                throw new InvalidOperationException(
                    $"Resume context contains Step '{pair.Key}', but that Step does not declare a runtime capture.");

            if (sourceStep.Order >= startStep.Value)
                throw new InvalidOperationException(
                    $"Resume capture for Step {sourceStep.Order} '{pair.Key}' is not earlier than requested start Step {startStep.Value}.");

            if (string.IsNullOrWhiteSpace(pair.Value))
                throw new InvalidOperationException(
                    $"Resume capture for Step '{pair.Key}' is empty.");
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadResumeContextAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("DAP resume context file was not found.", fullPath);

        await using var stream = File.OpenRead(fullPath);
        var values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
            stream,
            cancellationToken: cancellationToken);

        return values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    private static async Task<int> RunWindowsAsync(
        DapLaunchOptions options,
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        IReadOnlyList<DAP.Core.Guides.GuideApplicationContext> applicationContexts,
        IReadOnlyDictionary<string, string> resumeContext,
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {
        var windowsContexts = applicationContexts.Where(context => context.Runtime == DAP.Core.Targets.TargetRuntime.Windows).ToArray();
        AutomationElement window;
        if (windowsContexts.Length == 1)
            window = new WindowsApplicationContextResolver().Resolve(windowsContexts[0]);
        else if (windowsContexts.Length > 1)
            throw new NotSupportedException("Multiple Windows application contexts require per-step context resolution.");
        else if (!string.IsNullOrWhiteSpace(options.WindowAutomationId))
            window = await WaitForWindowAsync(options.WindowAutomationId, cancellationToken);
        else
            throw new InvalidOperationException("Windows Guide requires a persisted application context or --window-automation-id.");
        StartupMark(startup, "Windows target window resolved");
        await ActivateWindowsTargetAsync(window, cancellationToken);
        StartupMark(startup, "Windows target window activated");

        var resolver = new WindowsTargetResolver();
        var bubbles = new WindowsBubblePresenter(texts);
        var runtime = new WindowsGuideRuntime(resolver, bubbles, automaticStepLabel: options.ExecutionMode == DapExecutionMode.Hybrid ? "אוטומט" : null, hybrid: options.ExecutionMode == DapExecutionMode.Hybrid);

        try
        {
            StartupMark(startup, "Windows guide runtime starting");
            await runtime.RunAsync(window, steps, cancellationToken, options.StartStep, resumeContext);

        }
        finally
        {
            await bubbles.HideAsync();
        }

        return 0;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    private static async Task ActivateWindowsTargetAsync(AutomationElement target, CancellationToken cancellationToken)
    {
        var handle = new IntPtr(target.Current.NativeWindowHandle);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Resolved Windows application has no native window handle.");

        if (IsIconic(handle))
            ShowWindow(handle, 9); // SW_RESTORE

        SetForegroundWindow(handle);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetForegroundWindow() == handle)
                return;
            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException(
            "Windows did not activate the target application within five seconds. Activate the application and retry.");
    }

    private static async Task<AutomationElement> WaitForWindowAsync(
        string automationId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var condition = new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            automationId);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = AutomationElement.RootElement
                .FindAll(TreeScope.Children, condition)
                .Cast<AutomationElement>()
                .ToArray();

            if (matches.Length == 1)
                return matches[0];

            if (matches.Length > 1)
                throw new InvalidOperationException(
                    $"DAP.exe found {matches.Length} Windows target windows with AutomationId '{automationId}'; exactly one is required.");

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"DAP.exe did not find a Windows target window with AutomationId '{automationId}' within 30 seconds.");
    }

    private static async Task<int> RunWebAsync(
        DapLaunchOptions options,
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        IReadOnlyList<DAP.Core.Guides.GuideApplicationContext> applicationContexts,
        IReadOnlyDictionary<string, string> resumeContext,
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {

        using var browserAdapter = new ExtensionWebBrowserAdapter(texts);
        browserAdapter.ConfigureApplicationContexts(applicationContexts);
        var stepRuntime = new WebGuideStepRuntime(browserAdapter, automaticStepLabel: GetAutomaticStepLabel(), hybrid: options.ExecutionMode == DapExecutionMode.Hybrid);
        var guideRuntime = new AdapterWebGuideRuntime(stepRuntime, browserAdapter);
        StartupMark(startup, "Web extension adapter composition root created");

        try
        {
            StartupMark(startup, "Web adapter guide runtime starting");
            await guideRuntime.RunAsync(steps, cancellationToken, options.StartStep, resumeContext);
        }
        finally
        {
            await browserAdapter.HideBubbleAsync(CancellationToken.None);
        }

        return 0;
    }

    private static string? GetAutomaticStepLabel()
        => string.Equals(Environment.GetEnvironmentVariable("DAP_LEARNER_AUTOMATION"), "1", StringComparison.Ordinal)
            ? "אוטומט"
            : null;

}
