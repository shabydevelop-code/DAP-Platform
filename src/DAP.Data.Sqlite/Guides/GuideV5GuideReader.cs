using Microsoft.Data.Sqlite;

namespace DAP.Data.Sqlite.Guides;

/// <summary>
/// Read-only, experimental V5 aggregate. Validates references between step
/// definitions, observation rules and reusable UI elements. Not a legacy adapter.
/// </summary>
public static class GuideV5GuideReader
{
    public sealed record GuideDefinition(
        long GuideId,
        IReadOnlyList<GuideV5DefinitionReader.StepDefinition> Steps,
        GuideV5ElementReader.ElementCatalog ElementCatalog);

    public static async Task<GuideDefinition> ReadAsync(
        string databasePath, string guideKey, CancellationToken cancellationToken = default)
    {
        var steps = await GuideV5DefinitionReader.ReadStepsAsync(
            databasePath, guideKey, cancellationToken);
        var catalog = await GuideV5ElementReader.ReadAsync(
            databasePath, guideKey, cancellationToken);
        var stepIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in steps)
        {
            if (step.GuideId != catalog.GuideId)
                throw new InvalidOperationException(
                    $"V5 step '{step.StepId}' does not belong to the selected guide.");
            if (!stepIds.Add(step.StepId))
                throw new InvalidOperationException($"Duplicate V5 step '{step.StepId}'.");

            var primary = step.Signals.Count(signal =>
                string.Equals(signal.Role, "PrimaryValidation", StringComparison.Ordinal));
            if (string.Equals(step.AdvancePolicy, "AutomaticOnValidation", StringComparison.Ordinal)
                && step.IsEnabled && primary != 1)
                throw new InvalidOperationException(
                    $"Automatic V5 step '{step.StepId}' requires exactly one primary validation; found {primary}.");

            foreach (var signal in step.Signals)
            {
                if (signal.TargetUsageId is not null
                    && !catalog.Usages.Any(usage =>
                        usage.StepId == step.StepId && usage.UsageId == signal.TargetUsageId))
                    throw new InvalidOperationException(
                        $"V5 step '{step.StepId}' signal '{signal.SignalId}' references missing usage '{signal.TargetUsageId}'.");
            }
        }

        foreach (var usage in catalog.Usages)
        {
            if (!stepIds.Contains(usage.StepId))
                throw new InvalidOperationException(
                    $"V5 element usage '{usage.UsageId}' references unknown step '{usage.StepId}'.");
        }

        return new GuideDefinition(catalog.GuideId, steps, catalog);
    }
}
