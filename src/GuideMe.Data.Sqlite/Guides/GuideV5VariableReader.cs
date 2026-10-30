using GuideMe.Core.Guides;
using Microsoft.Data.Sqlite;

namespace GuideMe.Data.Sqlite.Guides;

/// <summary>Read canonical guide variable definitions; no legacy-capture fallback.</summary>
public static class GuideV5VariableReader
{
    public static async Task RequireMigratedAsync(
        string databasePath, long guideId, CancellationToken token = default)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await db.OpenAsync(token);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT
            (SELECT COUNT(*) FROM StepRules WHERE GuideId=$guide AND RuleType='Capture')
          + (SELECT COUNT(*) FROM Elements WHERE GuideId=$guide
             AND LocatorsJson LIKE '%{{step:%:capture}}%')
          + (SELECT COUNT(*) FROM Steps WHERE GuideId=$guide
             AND ContextValue LIKE '%{{step:%:capture}}%')";
        cmd.Parameters.AddWithValue("$guide", guideId);
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync(token)) != 0)
            throw new InvalidOperationException(
                "Guide contains legacy Capture data. Run the one-time V5 variable migration before starting the Learner.");
    }

    public static async Task<IReadOnlyList<GuideVariableDefinition>> ReadDefinitionsAsync(
        string databasePath, long guideId, CancellationToken token = default)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await db.OpenAsync(token);
        // New variable tables may not exist on a database that has not yet
        // undergone the one-time migration. Do not break existing guide startup.
        await using (var presence = db.CreateCommand())
        {
            presence.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            presence.Parameters.AddWithValue("$table", "GuideVariables");
            var expectedTable = "GuideVariables";
            presence.Parameters["$table"].Value = expectedTable;
            if (Convert.ToInt32(await presence.ExecuteScalarAsync(token)) == 0)
                return Array.Empty<GuideVariableDefinition>();
        }
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT VariableId,DisplayName,InitialValue FROM GuideVariables WHERE GuideId=$guide ORDER BY rowid";
        cmd.Parameters.AddWithValue("$guide", guideId);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        var definitions = new List<GuideVariableDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id))
                throw new InvalidOperationException("Invalid or duplicate guide variable.");
            definitions.Add(new GuideVariableDefinition(id, name,
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return definitions;
    }
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<GuideVariableAction>>> ReadActionsAsync(
        string databasePath, long guideId, CancellationToken token = default)
    {
        var definitions = await ReadDefinitionsAsync(databasePath, guideId, token);
        var ids = definitions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await db.OpenAsync(token);
        // New variable tables may not exist on a database that has not yet
        // undergone the one-time migration. Do not break existing guide startup.
        await using (var presence = db.CreateCommand())
        {
            presence.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            presence.Parameters.AddWithValue("$table", "StepVariableActions");
            var expectedTable = "StepVariableActions";
            presence.Parameters["$table"].Value = expectedTable;
            if (Convert.ToInt32(await presence.ExecuteScalarAsync(token)) == 0)
                return new Dictionary<string, IReadOnlyList<GuideVariableAction>>(StringComparer.Ordinal);
        }
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT StepId,ActionId,ActionOrder,VariableId,ActionKind,
            TargetUsageId,Property,Pattern,Timing
            FROM StepVariableActions WHERE GuideId=$guide ORDER BY StepId,ActionOrder";
        cmd.Parameters.AddWithValue("$guide", guideId);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        var byStep = new Dictionary<string, List<GuideVariableAction>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
        {
            var stepId = reader.GetString(0);
            var variableId = reader.GetString(3);
            if (!ids.Contains(variableId)
                || !Enum.TryParse<GuideVariableActionKind>(reader.GetString(4), out var kind)
                || kind != GuideVariableActionKind.ReadFromTarget
                || reader.IsDBNull(8))
                throw new InvalidOperationException($"Invalid variable action for step '{stepId}'.");
            string? Nullable(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
            var action = new GuideVariableAction(reader.GetString(1), variableId, kind,
                reader.GetInt32(2), Nullable(5),
                Nullable(6), Nullable(7), StepCaptureTiming.AfterAction);
            if (!byStep.TryGetValue(stepId, out var actions))
                byStep[stepId] = actions = new List<GuideVariableAction>();
            if (actions.Any(x => x.Order == action.Order || x.Id == action.Id))
                throw new InvalidOperationException($"Duplicate variable action in step '{stepId}'.");
            actions.Add(action);
        }
        return byStep.ToDictionary(x => x.Key,
            x => (IReadOnlyList<GuideVariableAction>)x.Value, StringComparer.Ordinal);
    }

}
