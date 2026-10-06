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

        if (await UsesLegacyTextIdsAsync(connection, cancellationToken))
            await MigrateLegacyTextIdsAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = Schema002;
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (!await HasColumnAsync(connection, "GuideSteps", "AutoFocusTarget", cancellationToken))
        {
            await using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE GuideSteps ADD COLUMN AutoFocusTarget INTEGER NOT NULL DEFAULT 0;";
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static async Task<bool> UsesLegacyTextIdsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Guides';";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) == 0)
            return false;

        await using var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(Guides);";
        await using var reader = await info.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1).Equals("Key", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static async Task MigrateLegacyTextIdsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
            await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Migration001To002;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private const string Migration001To002 = """
DROP INDEX IF EXISTS IX_GuideSteps_GuideId_StepOrder;
DROP INDEX IF EXISTS IX_TargetAnchors_GuideStepId;

ALTER TABLE TargetAnchors RENAME TO TargetAnchors_Legacy;
ALTER TABLE GuideSteps RENAME TO GuideSteps_Legacy;
ALTER TABLE Guides RENAME TO Guides_Legacy;

CREATE TABLE Guides (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Key TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL
);

CREATE TABLE GuideSteps (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GuideId INTEGER NOT NULL,
    Key TEXT NOT NULL,
    StepOrder INTEGER NOT NULL,
    AdvanceMode TEXT NOT NULL,
    Runtime TEXT NULL,
    LocatorStrategy TEXT NULL,
    LocatorValue TEXT NULL,
    FrameContextJson TEXT NULL,
    ContextKind TEXT NULL,
    ContextValue TEXT NULL,
    BubbleContent TEXT NOT NULL,
    BubblePlacement TEXT NOT NULL,
    ValidationKind TEXT NULL,
    ValidationExpectedValue TEXT NULL,
    ValidationOptionsJson TEXT NULL,
    FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE,
    UNIQUE (GuideId, Key),
    UNIQUE (GuideId, StepOrder)
);

CREATE TABLE TargetAnchors (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GuideStepId INTEGER NOT NULL,
    AnchorOrder INTEGER NOT NULL,
    Relation TEXT NOT NULL,
    LocatorStrategy TEXT NOT NULL,
    LocatorValue TEXT NOT NULL,
    FOREIGN KEY (GuideStepId) REFERENCES GuideSteps(Id) ON DELETE CASCADE,
    UNIQUE (GuideStepId, AnchorOrder)
);

INSERT INTO Guides(Key, Name)
SELECT Id, Name FROM Guides_Legacy ORDER BY rowid;

INSERT INTO GuideSteps(
    GuideId, Key, StepOrder, AdvanceMode, Runtime, LocatorStrategy, LocatorValue, FrameContextJson,
    ContextKind, ContextValue, BubbleContent, BubblePlacement, ValidationKind, ValidationExpectedValue, ValidationOptionsJson)
SELECT
    g.Id, s.Id, s.StepOrder, s.AdvanceMode, s.Runtime, s.LocatorStrategy, s.LocatorValue, s.FrameContextJson,
    s.ContextKind, s.ContextValue, s.BubbleContent, s.BubblePlacement, s.ValidationKind, s.ValidationExpectedValue, s.ValidationOptionsJson
FROM GuideSteps_Legacy s
JOIN Guides g ON g.Key = s.GuideId
ORDER BY s.rowid;

INSERT INTO TargetAnchors(Id, GuideStepId, AnchorOrder, Relation, LocatorStrategy, LocatorValue)
SELECT
    a.Id, ns.Id, a.AnchorOrder, a.Relation, a.LocatorStrategy, a.LocatorValue
FROM TargetAnchors_Legacy a
JOIN GuideSteps_Legacy os ON os.Id = a.GuideStepId
JOIN Guides g ON g.Key = os.GuideId
JOIN GuideSteps ns ON ns.GuideId = g.Id AND ns.Key = os.Id
ORDER BY a.Id;

DROP TABLE TargetAnchors_Legacy;
DROP TABLE GuideSteps_Legacy;
DROP TABLE Guides_Legacy;

CREATE INDEX IX_GuideSteps_GuideId_StepOrder ON GuideSteps(GuideId, StepOrder);
CREATE INDEX IX_TargetAnchors_GuideStepId ON TargetAnchors(GuideStepId);
""";

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
 ValidationOptionsJson TEXT NULL, AutoFocusTarget INTEGER NOT NULL DEFAULT 0,
 FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE,
 UNIQUE (GuideId, Key), UNIQUE (GuideId, StepOrder));
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
CREATE INDEX IF NOT EXISTS IX_StepCompletionConditions_GuideStepId
 ON StepCompletionConditions(GuideStepId);
CREATE INDEX IF NOT EXISTS IX_GuideSteps_GuideId_StepOrder ON GuideSteps(GuideId, StepOrder);
CREATE INDEX IF NOT EXISTS IX_TargetAnchors_GuideStepId ON TargetAnchors(GuideStepId);
""";
}
