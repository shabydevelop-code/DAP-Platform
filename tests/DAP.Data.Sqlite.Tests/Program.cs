using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

var root=Path.Combine(Path.GetTempPath(),"DAP.Tests",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await VerifyFreshRoundTripAsync(Path.Combine(root,"fresh.db"));
    await VerifyLegacyMigrationAsync(Path.Combine(root,"legacy.db"));
    Console.WriteLine("DAP SQLite guide persistence and legacy ID migration: PASS");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if(Directory.Exists(root))
        Directory.Delete(root,true);
}

static async Task VerifyFreshRoundTripAsync(string path)
{
    var factory=new SqliteConnectionFactory(new SqliteDatabaseOptions(path));
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository=new SqliteGuideStepRepository(factory);
    var step=CreateStep();

    await repository.SaveStepAsync("guide-1",step);
    var loaded=(await repository.GetStepsAsync("guide-1")).Single();
    if(loaded!=step)
        throw new Exception($"SQLite round-trip mismatch.\nExpected: {step}\nActual: {loaded}");

    await using(var connection=await factory.OpenAsync())
    {
        await using var command=connection.CreateCommand();
        command.CommandText="""
SELECT typeof(g.Id), typeof(s.Id), typeof(s.GuideId), typeof(a.GuideStepId)
FROM Guides g
JOIN GuideSteps s ON s.GuideId=g.Id
JOIN TargetAnchors a ON a.GuideStepId=s.Id
WHERE g.Key='guide-1' AND s.Key='step-1';
""";
        await using var reader=await command.ExecuteReaderAsync();
        if(!await reader.ReadAsync())
            throw new Exception("Fresh numeric-ID schema row was not found.");
        for(var i=0;i<4;i++)
            if(reader.GetString(i)!="integer")
                throw new Exception($"Expected numeric SQLite ID at column {i}, got {reader.GetString(i)}.");
    }
}

static async Task VerifyLegacyMigrationAsync(string path)
{
    var factory=new SqliteConnectionFactory(new SqliteDatabaseOptions(path));
    await using(var connection=await factory.OpenAsync())
    {
        await using var command=connection.CreateCommand();
        command.CommandText="""
PRAGMA foreign_keys=ON;
CREATE TABLE Guides (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
CREATE TABLE GuideSteps (
 Id TEXT PRIMARY KEY, GuideId TEXT NOT NULL, StepOrder INTEGER NOT NULL, AdvanceMode TEXT NOT NULL,
 Runtime TEXT NULL, LocatorStrategy TEXT NULL, LocatorValue TEXT NULL, FrameContextJson TEXT NULL,
 ContextKind TEXT NULL, ContextValue TEXT NULL, BubbleContent TEXT NOT NULL, BubblePlacement TEXT NOT NULL,
 ValidationKind TEXT NULL, ValidationExpectedValue TEXT NULL, ValidationOptionsJson TEXT NULL,
 FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE, UNIQUE (GuideId, StepOrder));
CREATE TABLE TargetAnchors (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, GuideStepId TEXT NOT NULL, AnchorOrder INTEGER NOT NULL,
 Relation TEXT NOT NULL, LocatorStrategy TEXT NOT NULL, LocatorValue TEXT NOT NULL,
 FOREIGN KEY (GuideStepId) REFERENCES GuideSteps(Id) ON DELETE CASCADE, UNIQUE (GuideStepId, AnchorOrder));
INSERT INTO Guides(Id,Name) VALUES('legacy-guide','Legacy Guide');
INSERT INTO GuideSteps(
 Id,GuideId,StepOrder,AdvanceMode,Runtime,LocatorStrategy,LocatorValue,FrameContextJson,
 ContextKind,ContextValue,BubbleContent,BubblePlacement,ValidationKind,ValidationExpectedValue,ValidationOptionsJson)
VALUES(
 'legacy-step','legacy-guide',1,'AutomaticOnValidation','Web','css','button.primary','[{"Strategy":"css","Value":"#content-frame"}]',
 NULL,NULL,'Legacy bubble','Right','value-equals','אלפא','{"trim":"true"}');
INSERT INTO TargetAnchors(GuideStepId,AnchorOrder,Relation,LocatorStrategy,LocatorValue)
VALUES('legacy-step',0,'Ancestor','css','#customer-search');
""";
        await command.ExecuteNonQueryAsync();
    }

    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository=new SqliteGuideStepRepository(factory);
    var steps=await repository.GetStepsAsync("legacy-guide");
    if(steps.Count!=1 || steps[0].Id!="legacy-step")
        throw new Exception("Legacy Guide/Step keys were not preserved during numeric-ID migration.");
    if(steps[0].Target?.Anchors.Count!=1)
        throw new Exception("Legacy TargetAnchor was not preserved during numeric-ID migration.");

    await using(var migrated=await factory.OpenAsync())
    {
        await using var verify=migrated.CreateCommand();
        verify.CommandText="""
SELECT typeof(g.Id), typeof(s.Id), typeof(s.GuideId), typeof(a.GuideStepId)
FROM Guides g
JOIN GuideSteps s ON s.GuideId=g.Id
JOIN TargetAnchors a ON a.GuideStepId=s.Id
WHERE g.Key='legacy-guide' AND s.Key='legacy-step';
""";
        await using var reader=await verify.ExecuteReaderAsync();
        if(!await reader.ReadAsync())
            throw new Exception("Migrated numeric-ID rows were not found.");
        for(var i=0;i<4;i++)
            if(reader.GetString(i)!="integer")
                throw new Exception($"Legacy migration left a non-numeric ID at column {i}.");
    }
}

static GuideStep CreateStep()=>new(
    "step-1",1,
    TargetDescriptor.Create(TargetRuntime.Web,new Locator("css","button.primary"),new[] {
        new Anchor(new Locator("css","#customer-search"),AnchorRelation.Ancestor),
        new Anchor(new Locator("css","[data-context='customer']"),AnchorRelation.Nearby)
    },new FrameContext(new[] { new Locator("css","#content-frame") })),
    new BubbleDefinition("הקלד את שם הלקוח",BubblePlacement.Right),
    new ValidationDefinition("value-equals","אלפא",new Dictionary<string,string>{{"trim","true"}}));
