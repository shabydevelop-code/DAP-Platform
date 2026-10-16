using Microsoft.Data.Sqlite;

namespace DAP.Data.Sqlite.Guides;

/// <summary>
/// Read-only inspection of the experimental V5 definition model.
/// This does not activate V5 in the production learner.
/// </summary>
public static class GuideV5DefinitionReader
{
    public sealed record ObservedSignal(
        string SignalId, int Order, string Role, string Kind,
        string? TargetUsageId, string? ExpectedValue, string? OptionsJson);

    public sealed record StepDefinition(
        long GuideId, string StepId, int Order, bool IsEnabled,
        string Instruction, string InteractionKind, bool InteractionConfirmed,
        string OutcomeKind, string OutcomeStatus, string AdvancePolicy,
        IReadOnlyList<ObservedSignal> Signals);

    public static async Task<IReadOnlyList<StepDefinition>> ReadStepsAsync(
        string databasePath, string guideKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(guideKey);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        var steps = new List<StepDefinition>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.GuideId,s.StepId,s.StepOrder,s.IsEnabled,
                       b.Content,i.InteractionKind,i.IsAuthorConfirmed,
                       o.OutcomeKind,o.DefinitionStatus,a.PolicyKind
                FROM GuideSteps s
                JOIN Guides g ON g.GuideId=s.GuideId
                JOIN StepBubbles b ON b.GuideId=s.GuideId AND b.StepId=s.StepId
                JOIN StepInteractions i ON i.GuideId=s.GuideId AND i.StepId=s.StepId
                JOIN StepExpectedOutcomes o ON o.GuideId=s.GuideId AND o.StepId=s.StepId
                JOIN StepAdvancePolicies a ON a.GuideId=s.GuideId AND a.StepId=s.StepId
                WHERE g.GuideKey=$guideKey
                ORDER BY s.StepOrder;
                """;
            command.Parameters.AddWithValue("$guideKey", guideKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                steps.Add(new StepDefinition(
                    reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetInt64(3) != 0, reader.GetString(4), reader.GetString(5),
                    reader.GetInt64(6) != 0, reader.GetString(7), reader.GetString(8),
                    reader.GetString(9), Array.Empty<ObservedSignal>()));
            }
        }

        if (steps.Count == 0)
            throw new InvalidOperationException($"V5 guide '{guideKey}' has no complete steps.");

        var completed = new List<StepDefinition>(steps.Count);
        foreach (var step in steps)
        {
            var signals = new List<ObservedSignal>();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT SignalId,SignalOrder,SignalRole,Kind,TargetUsageId,ExpectedValue,OptionsJson
                FROM StepObservedSignals
                WHERE GuideId=$guideId AND StepId=$stepId
                ORDER BY SignalOrder;
                """;
            command.Parameters.AddWithValue("$guideId", step.GuideId);
            command.Parameters.AddWithValue("$stepId", step.StepId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                signals.Add(new ObservedSignal(reader.GetString(0), reader.GetInt32(1),
                    reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
            completed.Add(step with { Signals = signals });
        }
        return completed;
    }
}
