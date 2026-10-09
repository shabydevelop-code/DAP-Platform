using Microsoft.Data.Sqlite;

namespace DAP.Data.Sqlite;

public sealed class SqliteDatabaseInitializer
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteDatabaseInitializer(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = Schema002;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureGuideStepHybridColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureGuideStepHybridColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(GuideSteps);";
            await using var reader = await info.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("IsEnabled"))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE GuideSteps ADD COLUMN IsEnabled INTEGER NOT NULL DEFAULT 1;";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!columns.Contains("AutomationValue"))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE GuideSteps ADD COLUMN AutomationValue TEXT NULL;";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!columns.Contains("ApplicationContextKey"))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE GuideSteps ADD COLUMN ApplicationContextKey TEXT NULL;";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private const string Schema002 = """
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS Guides (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, Key TEXT NOT NULL UNIQUE, Name TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS GuideSteps (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, GuideId INTEGER NOT NULL, Key TEXT NOT NULL,
 StepOrder INTEGER NOT NULL, AdvanceMode TEXT NOT NULL, Runtime TEXT NULL,
 LocatorStrategy TEXT NULL, LocatorValue TEXT NULL, FrameContextJson TEXT NULL,
 ContextKind TEXT NULL, ContextValue TEXT NULL, BubbleContent TEXT NOT NULL,
 BubblePlacement TEXT NOT NULL, ValidationKind TEXT NULL, ValidationExpectedValue TEXT NULL,
 ValidationOptionsJson TEXT NULL, IsEnabled INTEGER NOT NULL DEFAULT 1, AutomationValue TEXT NULL,
 ApplicationContextKey TEXT NULL,
 FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE,
 UNIQUE (GuideId, Key), UNIQUE (GuideId, StepOrder));
CREATE TABLE IF NOT EXISTS GuideApplicationContexts (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, GuideId INTEGER NOT NULL, Key TEXT NOT NULL, Runtime TEXT NOT NULL,
 FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE,
 UNIQUE (GuideId, Key));
CREATE TABLE IF NOT EXISTS ApplicationContextMatchers (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, ApplicationContextId INTEGER NOT NULL, MatcherOrder INTEGER NOT NULL,
 Kind TEXT NOT NULL, Value TEXT NOT NULL,
 FOREIGN KEY (ApplicationContextId) REFERENCES GuideApplicationContexts(Id) ON DELETE CASCADE,
 UNIQUE (ApplicationContextId, MatcherOrder));
CREATE TABLE IF NOT EXISTS TargetAnchors (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, GuideStepId INTEGER NOT NULL, AnchorOrder INTEGER NOT NULL,
 Relation TEXT NOT NULL, LocatorStrategy TEXT NOT NULL, LocatorValue TEXT NOT NULL,
 FOREIGN KEY (GuideStepId) REFERENCES GuideSteps(Id) ON DELETE CASCADE,
 UNIQUE (GuideStepId, AnchorOrder));
CREATE TABLE IF NOT EXISTS StepCaptures (
 GuideStepId INTEGER PRIMARY KEY, Runtime TEXT NOT NULL, LocatorStrategy TEXT NOT NULL,
 LocatorValue TEXT NOT NULL, Property TEXT NOT NULL, Pattern TEXT NULL,
 FOREIGN KEY (GuideStepId) REFERENCES GuideSteps(Id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS StepCompletionConditions (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, GuideStepId INTEGER NOT NULL, ConditionOrder INTEGER NOT NULL,
 Kind TEXT NOT NULL, ExpectedValue TEXT NULL, TargetJson TEXT NOT NULL,
 FOREIGN KEY (GuideStepId) REFERENCES GuideSteps(Id) ON DELETE CASCADE,
 UNIQUE (GuideStepId, ConditionOrder));
CREATE INDEX IF NOT EXISTS IX_ApplicationContextMatchers_ApplicationContextId
 ON ApplicationContextMatchers(ApplicationContextId);
CREATE INDEX IF NOT EXISTS IX_GuideApplicationContexts_GuideId
 ON GuideApplicationContexts(GuideId);
CREATE INDEX IF NOT EXISTS IX_StepCompletionConditions_GuideStepId
 ON StepCompletionConditions(GuideStepId);
CREATE INDEX IF NOT EXISTS IX_GuideSteps_GuideId_StepOrder ON GuideSteps(GuideId, StepOrder);
CREATE INDEX IF NOT EXISTS IX_TargetAnchors_GuideStepId ON TargetAnchors(GuideStepId);
""";
}
