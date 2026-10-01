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

        // Browser attachment is deliberately explicit. DAP.exe must own or attach
        // to the browser connection; it cannot reuse an in-process Playwright IPage
        // created by another executable.
        Console.Error.WriteLine("Learner Web launch requires a browser attachment endpoint. This contract is the next implementation step.");
        _ = repository;
        return 3;
    }
}
