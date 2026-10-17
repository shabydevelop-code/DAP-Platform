using Microsoft.Data.Sqlite;

namespace GuideMe.Data.Sqlite.Guides;

/// <summary>Loads application contexts, variable identities, and captures for the production learner.</summary>
public static class GuideV5ContextCaptureReader
{
    public sealed record ContextMatcher(int Order, string Kind, string Value);
    public sealed record ApplicationContext(string ContextId, string Runtime, IReadOnlyList<ContextMatcher> Matchers);
    public sealed record Variable(string VariableId);
    public sealed record Capture(string StepId, string CaptureId, string VariableId, string TargetUsageId,
        string Property, string? Pattern, string? Timing);
    public sealed record Definitions(long GuideId, IReadOnlyList<ApplicationContext> Contexts,
        IReadOnlyList<Variable> Variables, IReadOnlyList<Capture> Captures);

    public static async Task<Definitions> ReadAsync(
        string databasePath, string guideKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(guideKey);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync(cancellationToken);

        long guideId;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT GuideId FROM Guides WHERE GuideKey=$key";
            command.Parameters.AddWithValue("$key", guideKey);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null || result is DBNull)
                throw new InvalidOperationException($"V5 guide '{guideKey}' was not found.");
            guideId = Convert.ToInt64(result);
        }

        var contexts = new List<ApplicationContext>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ContextId,Runtime FROM GuideApplicationContexts WHERE GuideId=$guide ORDER BY ContextId";
            command.Parameters.AddWithValue("$guide", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                contexts.Add(new ApplicationContext(reader.GetString(0), reader.GetString(1), Array.Empty<ContextMatcher>()));
        }

        var loadedContexts = new List<ApplicationContext>(contexts.Count);
        foreach (var context in contexts)
        {
            var matchers = new List<ContextMatcher>();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MatcherOrder,Kind,Value FROM ApplicationContextMatchers
                WHERE GuideId=$guide AND ContextId=$context ORDER BY MatcherOrder
                """;
            command.Parameters.AddWithValue("$guide", guideId);
            command.Parameters.AddWithValue("$context", context.ContextId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                matchers.Add(new ContextMatcher(reader.GetInt32(0),
                    reader.GetString(1), reader.GetString(2)));
            loadedContexts.Add(context with { Matchers = matchers });
        }

        var variables = new List<Variable>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT VariableId FROM GuideVariables WHERE GuideId=$guide ORDER BY VariableId";
            command.Parameters.AddWithValue("$guide", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                variables.Add(new Variable(reader.GetString(0)));
        }

        var captures = new List<Capture>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT StepId,CaptureId,VariableId,TargetUsageId,Property,Pattern,Timing
                FROM StepCaptures WHERE GuideId=$guide ORDER BY StepId,CaptureId
                """;
            command.Parameters.AddWithValue("$guide", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                captures.Add(new Capture(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return new Definitions(guideId, loadedContexts, variables, captures);
    }
}
