using System.IO;
using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;
using Microsoft.Data.Sqlite;
using System.Windows.Automation;
using System.Diagnostics;

if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
{
    var watch = Stopwatch.StartNew();
    var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
    Console.WriteLine($"Top-level windows: {windows.Count}");
    for (var i = 0; i < windows.Count; i++)
    {
        var window = windows[i];
        AutomationElement? grid;
        try { grid = window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CasesGrid")); }
        catch (ElementNotAvailableException) { continue; }
        if (grid is null) continue;
        Console.WriteLine($"Grid found in window: {window.Current.Name}, elapsed={watch.ElapsedMilliseconds}ms");
        var rows = grid.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        Console.WriteLine($"UIA DataItem rows={rows.Count}, elapsed={watch.ElapsedMilliseconds}ms");
        var exact = new PropertyCondition(AutomationElement.NameProperty, "DAP-GRID-BASELINE-0050");
        var cell = grid.FindFirst(TreeScope.Descendants, exact);
        Console.WriteLine($"Exact cell name found={cell is not null}, elapsed={watch.ElapsedMilliseconds}ms");
        for (var j = 0; j < rows.Count; j++)
        {
            var row = rows[j];
            try
            {
                var name = row.Current.Name;
                if (name.Contains("0050", StringComparison.Ordinal) || j < 3)
                {
                    Console.WriteLine($"row[{j}] name='{name}', id='{row.Current.AutomationId}'");
                    var descendants = row.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                    for (var k = 0; k < descendants.Count && k < 30; k++)
                    {
                        var element = descendants[k];
                        Console.WriteLine($"  child[{k}] type={element.Current.ControlType.ProgrammaticName}, name='{element.Current.Name}', id='{element.Current.AutomationId}'");
                    }
                }
            }
            catch (ElementNotAvailableException) { }
        }
        Console.WriteLine($"Probe completed in {watch.ElapsedMilliseconds}ms");
        return;
    }
    Console.WriteLine("CasesGrid not found in any top-level window.");
    return;
}

// Web baseline shares the existing 100 demo records but installs an independent Guide.
if (args.Contains("--web", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--remove-web", StringComparer.OrdinalIgnoreCase))
{
    const string webGuideKey = "sampleapp-web-grid-baseline-temporary";
    var webRepository = new SqliteGuideStepRepository(
        new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()));
    if (args.Contains("--remove-web", StringComparer.OrdinalIgnoreCase))
    {
        await using var db = await new SqliteConnectionFactory(
            SqliteDatabaseOptions.CreateDefault()).OpenAsync();
        await using var delete = db.CreateCommand();
        delete.CommandText = "DELETE FROM Guides WHERE Key=$key;";
        delete.Parameters.AddWithValue("$key", webGuideKey);
        Console.WriteLine($"Temporary Web Guide removed: {await delete.ExecuteNonQueryAsync()}");
        return;
    }

    var webContexts = await webRepository.GetApplicationContextsAsync("sampleapp-web-guide");
    var webContext = webContexts.FirstOrDefault(x => x.Runtime == TargetRuntime.Web)
        ?? throw new InvalidOperationException("Source Web Guide has no Web application context.");
    var webTarget = TargetDescriptor.Create(TargetRuntime.Web,
        new Locator("css", "tr:has-text('DAP-GRID-BASELINE-0050')"));
    var webStep = new GuideStep("web-grid-baseline-target", 1, webTarget,
        new BubbleDefinition("בדיקת Web: פנייה 0050 מתוך 100. בדוק גלילה, חזרה, יציבות ומיון.", BubblePlacement.Auto),
        AdvanceMode: StepAdvanceMode.Manual, ApplicationContextKey: webContext.Key);
    await webRepository.ReplaceApplicationContextsAsync(webGuideKey, [webContext]);
    await webRepository.ReplaceStepsAsync(webGuideKey, [webStep]);
    Console.WriteLine($"Temporary Web Guide installed: {webGuideKey}");
    Console.WriteLine("Open the SampleApp Web Cases tab for SiteId 1 before starting DAP.");
    Console.WriteLine("Remove later with --remove-web (does not remove the shared 100 records).");
    return;
}

const string guideKey = "sampleapp-windows-grid-baseline-temporary";
const string sourceGuideKey = "sampleapp-windows-guide";
var repo = new SqliteGuideStepRepository(new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()));
var remove = args.Contains("--remove", StringComparer.OrdinalIgnoreCase);
if (remove)
{
    await using var connection = await new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()).OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM Guides WHERE Key=$key;";
    command.Parameters.AddWithValue("$key", guideKey);
    var affected = await command.ExecuteNonQueryAsync();
    Console.WriteLine($"Temporary Guide removed: {affected}");
    var demoDb = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "demos", "Shared", "data", "sampleapp.db"));
    if (File.Exists(demoDb))
    {
        await using var demo = new SqliteConnection($"Data Source={demoDb}");
        await demo.OpenAsync();
        await using var delete = demo.CreateCommand();
        delete.CommandText = "DELETE FROM Cases WHERE SiteId=1 AND Subject LIKE 'DAP-GRID-BASELINE-%';";
        var removed = await delete.ExecuteNonQueryAsync();
        Console.WriteLine($"Temporary demo cases removed: {removed}");
    }
    return;
}
var contexts = await repo.GetApplicationContextsAsync(sourceGuideKey);
if (contexts.Count == 0)
    throw new InvalidOperationException($"Source Guide '{sourceGuideKey}' has no application contexts. Baseline not installed.");
var windowsContext = contexts.FirstOrDefault(x => x.Runtime == TargetRuntime.Windows)
    ?? throw new InvalidOperationException("Source Guide has no Windows application context.");
// A DataItem is identified within the CasesGrid by the unique subject of its
// descendant cell, rather than by a row index. The target remains runtime-resolved.
var target = TargetDescriptor.Create(TargetRuntime.Windows,
    new Locator("control-type", "dataitem"),
    [new Anchor(new Locator("automation-id", "CasesGrid"), AnchorRelation.Ancestor),
     new Anchor(new Locator("name-regex", "^DAP-GRID-BASELINE-0050$"), AnchorRelation.Descendant)]);
var step = new GuideStep("grid-baseline-target", 1, target,
    new BubbleDefinition("בדיקת בסיס: פנייה 0050 מתוך 100. בדוק זמן הופעה ויציבות לאחר מיון.", BubblePlacement.Auto),
    AdvanceMode: StepAdvanceMode.Manual, ApplicationContextKey: windowsContext.Key);
await repo.ReplaceApplicationContextsAsync(guideKey, [windowsContext]);
await repo.ReplaceStepsAsync(guideKey, [step]);
Console.WriteLine($"Temporary Guide installed: {guideKey}");
Console.WriteLine("Open the SampleApp Cases tab for SiteId=1 before starting DAP.");
Console.WriteLine("Remove after tests: dotnet run --project demos/Windows/GridBaseline/DAP.SampleApp.GridBaseline.csproj -- --remove");
