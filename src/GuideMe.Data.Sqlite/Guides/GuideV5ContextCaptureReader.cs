using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace GuideMe.Data.Sqlite.Guides;

/// <summary>Loads application contexts and captures for the production learner.</summary>
public static class GuideV5ContextCaptureReader
{
    public sealed record ContextMatcher(int Order, string Kind, string Value);
    public sealed record ApplicationContext(string ContextId, string Runtime, IReadOnlyList<ContextMatcher> Matchers);
    public sealed record Capture(string StepId, string CaptureId, string TargetUsageId,
        string Property, string? Pattern, string? Timing);
    public sealed record Definitions(long GuideId, IReadOnlyList<ApplicationContext> Contexts,
        IReadOnlyList<Capture> Captures);

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
            command.CommandText = "SELECT ContextId,Runtime,MatchersJson FROM ApplicationContexts WHERE GuideId=$guide ORDER BY ContextId";
            command.Parameters.AddWithValue("$guide", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                contexts.Add(new ApplicationContext(reader.GetString(0), reader.GetString(1), JsonSerializer.Deserialize<List<ContextMatcher>>(reader.GetString(2))
                        ?? throw new InvalidOperationException("Invalid compact matchers JSON.")));
        }

        var captures = new List<Capture>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT StepId,RuleId,TargetUsageId,Property,Pattern,Timing FROM StepRules WHERE GuideId=$guide AND RuleType='Capture' ORDER BY StepId,RuleId";
            command.Parameters.AddWithValue("$guide", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                captures.Add(new Capture(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return new Definitions(guideId, contexts, captures);
    }
}
