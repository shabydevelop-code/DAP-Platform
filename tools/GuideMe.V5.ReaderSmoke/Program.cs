using GuideMe.Data.Sqlite.Guides;

var db = args.Length > 0
    ? args[0]
    : @"C:\ProgramData\GuideMe\Data\GuideMe-V5.db";

var cases = new (string Key, long Id, int Web, int Windows)[]
{
    ("sampleapp-web-guide", 1, 98, 0),
    ("sampleapp-windows-guide", 55, 0, 118),
    ("sampleapp-mixed-smoke-guide", 965, 7, 3)
};

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
try
{
    foreach (var test in cases)
    {
        var catalog = await GuideV5ElementReader.ReadAsync(db, test.Key, timeout.Token);
        var web = catalog.Elements.Values.Count(e => e.Runtime == "Web");
        var windows = catalog.Elements.Values.Count(e => e.Runtime == "Windows");
        if (catalog.GuideId != test.Id || web != test.Web || windows != test.Windows ||
            catalog.Elements.Count != web + windows)
        {
            throw new InvalidOperationException(
                $"FAIL {test.Key}: GuideId={catalog.GuideId}, Web={web}, Windows={windows}, Total={catalog.Elements.Count}");
        }
        Console.WriteLine($"PASS | {test.Key} | Elements={catalog.Elements.Count} | Targets={catalog.Usages.Count} | Web={web} | Windows={windows}");
    }
    Console.WriteLine("PASS | All 3 guides loaded using GuideV5ElementReader");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL | {ex}");
    Environment.ExitCode = 1;
}
