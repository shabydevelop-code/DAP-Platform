using Microsoft.Playwright;
using System.IO;
using System.Windows;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;
using DAP.Runtime.Web.Bubbles;
using DAP.Runtime.Web.Learner;
using DAP.Runtime.Web.Targets;

namespace DAP.App;

public static class DapApplicationHost
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var options = DapLaunchOptions.Parse(args);
        if (options is null)
            return 2;

        var databaseOptions = SqliteDatabaseOptions.CreateDefault();
        var connections = new SqliteConnectionFactory(databaseOptions);
        await new SqliteDatabaseInitializer(connections).InitializeAsync(cancellationToken);

        // Production composition root. These objects now belong to DAP.exe rather
        // than to a target application or test process.
        var repository = new SqliteGuideStepRepository(connections);
        var resolver = new WebTargetResolver();
        var bubbles = new WebBubblePresenter(resolver);
        _ = new WebLearnerRuntime(bubbles);

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
                $"DAP.exe ready.\n\nDatabase: {databaseOptions.DatabasePath}\n\nDiagnostics: {diagnosticsPath}",
                "DAP Infrastructure Check",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return 0;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(
            options.CdpEndpoint!,
            new BrowserTypeConnectOverCDPOptions { Timeout = 10_000 });

        var pages = browser.Contexts.SelectMany(context => context.Pages).ToArray();
        var matchingPages = string.IsNullOrWhiteSpace(options.PageUrlContains)
            ? pages
            : pages.Where(page => page.Url.Contains(options.PageUrlContains, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (matchingPages.Length != 1)
        {
            MessageBox.Show(
                $"DAP.exe found {matchingPages.Length} matching browser pages; exactly one is required.",
                "DAP Learner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 4;
        }

        var steps = await repository.GetStepsAsync(options.GuideId!, cancellationToken);
        if (steps.Count == 0)
        {
            MessageBox.Show(
                $"Guide '{options.GuideId}' has no steps.",
                "DAP Learner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 5;
        }

        var stepRuntime = new WebLearnerRuntime(bubbles);
        var guideRuntime = new WebGuideRuntime(stepRuntime);
        try
        {
            await guideRuntime.RunAsync(matchingPages[0], steps, cancellationToken);
        }
        finally
        {
            await stepRuntime.StopAsync(matchingPages[0]);
        }

        return 0;
    }
}
