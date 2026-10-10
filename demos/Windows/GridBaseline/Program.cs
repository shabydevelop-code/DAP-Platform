using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

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
