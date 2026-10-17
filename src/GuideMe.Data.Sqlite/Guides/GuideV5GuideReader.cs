namespace GuideMe.Data.Sqlite.Guides;

/// <summary>
/// Validates the production V5 learner definition and cross-references between
/// steps, observation rules, captures, and reusable UI elements.
/// </summary>
public static class GuideV5GuideReader
{
    public sealed record GuideDefinition(
        long GuideId,
        IReadOnlyList<GuideV5DefinitionReader.StepDefinition> Steps,
        GuideV5ElementReader.ElementCatalog ElementCatalog,
        GuideV5ContextCaptureReader.Definitions ContextsAndCaptures);

    public static async Task<GuideDefinition> ReadAsync(
        string databasePath, string guideKey, CancellationToken cancellationToken = default)
    {
        var steps = await GuideV5DefinitionReader.ReadStepsAsync(
            databasePath, guideKey, cancellationToken);
        var catalog = await GuideV5ElementReader.ReadAsync(
            databasePath, guideKey, cancellationToken);
        var additional = await GuideV5ContextCaptureReader.ReadAsync(
            databasePath, guideKey, cancellationToken);
        if (additional.GuideId != catalog.GuideId)
            throw new InvalidOperationException("V5 context/capture guide identity mismatch.");
        var usageIds = catalog.Usages.Select(x => (x.StepId, x.UsageId)).ToHashSet();
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
            var hasPrimaryTarget = catalog.Usages.Any(usage =>
                string.Equals(usage.StepId, step.StepId, StringComparison.Ordinal)
                && string.Equals(usage.Purpose, "Primary", StringComparison.Ordinal));
            if (hasPrimaryTarget && step.IsEnabled && primary != 1)
                throw new InvalidOperationException(
                    $"Automatic V5 step '{step.StepId}' requires exactly one primary validation; found {primary}.");

            foreach (var signal in step.Signals)
            {
                if (signal.TargetUsageId is not null
                    && !usageIds.Contains((step.StepId, signal.TargetUsageId)))
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

        foreach (var capture in additional.Captures)
        {
            if (!stepIds.Contains(capture.StepId)
                || !usageIds.Contains((capture.StepId, capture.TargetUsageId)))
                throw new InvalidOperationException(
                    $"V5 capture '{capture.CaptureId}' has an unresolved step or element usage.");
        }

        return new GuideDefinition(catalog.GuideId, steps, catalog, additional);
    }
}
