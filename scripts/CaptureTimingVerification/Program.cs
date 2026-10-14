using DAP.Core.Guides;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;
using Microsoft.Data.Sqlite;

var original = SqliteDatabaseOptions.CreateDefault().DatabasePath;
if (!File.Exists(original))
    throw new FileNotFoundException("Source GuideMe database was not found.", original);

var temporary = Path.Combine(Path.GetTempPath(), $"guideme-capture-verification-{Guid.NewGuid():N}.db");
try
{
    // SQLite's backup API gives a consistent snapshot, including databases using WAL.
    using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
           { DataSource = original, Mode = SqliteOpenMode.ReadOnly }.ToString()))
    using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
           { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate }.ToString()))
    {
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    var options = new SqliteDatabaseOptions(temporary);
    var factory = new SqliteConnectionFactory(options);
    await new SqliteDatabaseInitializer(factory).InitializeAsync();
    var repository = new SqliteGuideStepRepository(factory);

    var scenarios = new[]
    {
        ("sampleapp-web-guide", StepCaptureTiming.BeforeAction),
        ("sampleapp-windows-guide", StepCaptureTiming.AfterAction)
    };
    foreach (var (guide, timing) in scenarios)
    {
        var steps = await repository.GetStepsAsync(guide);
        var step = steps.Single(s => s.Capture is not null);
        if (step.Order != 11 || step.Capture!.Timing is not null)
            throw new InvalidOperationException($"Unexpected original capture in {guide}.");

        await repository.SaveStepAsync(guide, step with
        {
            Capture = step.Capture with { Timing = timing }
        });
        var reloaded = (await repository.GetStepsAsync(guide)).Single(s => s.Id == step.Id);
        if (reloaded.Capture?.Timing != timing)
            throw new InvalidOperationException($"Capture timing round-trip failed for {guide}.");
        Console.WriteLine($"PASS: {guide}, step {step.Order}, timing {timing}");
    }

    // Null timing must survive a full repository save/load round trip.
    var legacyGuide = scenarios[0].Item1;
    var legacyStep = (await repository.GetStepsAsync(legacyGuide)).Single(s => s.Capture is not null);
    await repository.SaveStepAsync(legacyGuide, legacyStep with
    {
        Capture = legacyStep.Capture! with { Timing = null }
    });
    var legacyReloaded = (await repository.GetStepsAsync(legacyGuide)).Single(s => s.Id == legacyStep.Id);
    if (legacyReloaded.Capture?.Timing is not null)
        throw new InvalidOperationException("Legacy null capture timing was not preserved.");
    Console.WriteLine("PASS: legacy null timing preserved");
    Console.WriteLine("PASS: all changes were confined to a temporary database");
}
finally
{
    // Explicitly closed connections above; no open repository connections survive each awaited call.
    SqliteConnection.ClearAllPools();
    foreach (var path in new[] { temporary, temporary + "-wal", temporary + "-shm" })
        if (File.Exists(path))
            File.Delete(path);
}
