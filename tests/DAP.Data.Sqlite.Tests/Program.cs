using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

var path=Path.Combine(Path.GetTempPath(),"DAP.Tests",Guid.NewGuid()+".db");
try
{
    var factory=new SqliteConnectionFactory(new SqliteDatabaseOptions(path));
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository=new SqliteGuideStepRepository(factory);
    var step=new GuideStep("step-1",1,
        TargetDescriptor.Create(TargetRuntime.Web,new Locator("css","button.primary"),new[] {
            new Anchor(new Locator("css","#customer-search"),AnchorRelation.Ancestor),
            new Anchor(new Locator("css","[data-context='customer']"),AnchorRelation.Nearby)
        },new FrameContext(new[] { new Locator("css","#content-frame") })),
        new BubbleDefinition("הקלד את שם הלקוח",BubblePlacement.Right),
        new ValidationDefinition("value-equals","אלפא",new Dictionary<string,string>{{"trim","true"}}));
    await repository.SaveStepAsync("guide-1",step);
    var loaded=(await repository.GetStepsAsync("guide-1")).Single();
    if(loaded!=step) throw new Exception($"SQLite round-trip mismatch.\nExpected: {step}\nActual: {loaded}");
    Console.WriteLine("DAP SQLite guide persistence round-trip: PASS");
}
finally
{
    if(File.Exists(path)) File.Delete(path);
    var dir=Path.GetDirectoryName(path); if(dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
}
