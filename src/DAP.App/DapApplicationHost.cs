using Microsoft.Playwright;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using DAP.Data.Sqlite;
using DAP.App.Localization;
using DAP.Core.Localization;
using DAP.Data.Sqlite.Guides;
using DAP.Runtime.Web.Bubbles;
using DAP.Runtime.Web.Learner;
using DAP.Runtime.Web.Targets;
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
        var options = DapLaunchOptions.Parse(args);
        if (options is null)
            return 2;
        StartupMark(startup, "launch options parsed");

        IUiTextProvider texts = JsonUiTextProvider.LoadFromApplicationDirectory();
        StartupMark(startup, $"localization loaded ({texts.Language})");

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
        StartupMark(startup, $"guide loaded ({steps.Count} steps)");
        if (steps.Count == 0)
        {
            MessageBox.Show(
                texts.Format("Learner.GuideHasNoSteps", options.GuideId!),
                texts.Get("Learner.WindowTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 5;
        }

        if (options.Mode == DapLaunchMode.LearnerWindows)
            return await RunWindowsAsync(options, steps, texts, startup, cancellationToken);

        return await RunWebAsync(options, steps, texts, startup, cancellationToken);
    }

    private static async Task<int> RunWindowsAsync(
        DapLaunchOptions options,
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {
        var window = await WaitForWindowAsync(options.WindowAutomationId!, cancellationToken);
        StartupMark(startup, $"Windows target window resolved ({options.WindowAutomationId})");

        var resolver = new WindowsTargetResolver();
        var bubbles = new WindowsBubblePresenter(texts);
        var runtime = new WindowsGuideRuntime(resolver, bubbles);

        try
        {
            StartupMark(startup, "Windows guide runtime starting");
            await runtime.RunAsync(window, steps, cancellationToken, options.StartStep);

            if (options.ShowCompletion)
            {
                MessageBox.Show(
                    texts.Get("Learner.CompletedMessage"),
                    texts.Get("Learner.WindowTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            await bubbles.HideAsync();
        }

        return 0;
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
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {
        var resolver = new WebTargetResolver();
        var bubbles = new WebBubblePresenter(resolver, texts);
        StartupMark(startup, "Web composition root created");

        using var playwright = await Playwright.CreateAsync();
        StartupMark(startup, "Playwright created");
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(
            options.CdpEndpoint!,
            new BrowserTypeConnectOverCDPOptions { Timeout = 10_000 });
        StartupMark(startup, "Chromium CDP connected");

        var pages = browser.Contexts.SelectMany(context => context.Pages).ToArray();
        var matchingPages = string.IsNullOrWhiteSpace(options.PageUrlContains)
            ? pages
            : pages.Where(page => page.Url.Contains(options.PageUrlContains, StringComparison.OrdinalIgnoreCase)).ToArray();

        StartupMark(startup, $"browser page selected ({matchingPages.Length} match)");
        if (matchingPages.Length != 1)
        {
            MessageBox.Show(
                texts.Format("Learner.BrowserPageCountError", matchingPages.Length),
                texts.Get("Learner.WindowTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 4;
        }

        var stepRuntime = new WebLearnerRuntime(bubbles);
        var guideRuntime = new WebGuideRuntime(stepRuntime);
        try
        {
            StartupMark(startup, "Web guide runtime starting");
            await guideRuntime.RunAsync(matchingPages[0], steps, cancellationToken, options.StartStep);

            if (options.ShowCompletion)
            {
                MessageBox.Show(
                    texts.Get("Learner.CompletedMessage"),
                    texts.Get("Learner.WindowTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            await stepRuntime.StopAsync(matchingPages[0]);
        }

        return 0;
    }
}
