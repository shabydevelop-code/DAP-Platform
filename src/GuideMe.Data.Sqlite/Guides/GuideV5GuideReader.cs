namespace GuideMe.Data.Sqlite.Guides;

/// <summary>
/// Validates the production V5 learner definition and cross-references between
/// steps, observation rules, and reusable UI elements.
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
            throw new InvalidOperationException("V5 application context guide identity mismatch.");
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
            // Zero primary validations means explicit learner confirmation,
            // whether the step has a target-attached or centered bubble.
            // More than one primary validation is always ambiguous.
            if (primary > 1)
                throw new InvalidOperationException(
                    $"V5 step '{step.StepId}' has multiple primary validations ({primary}).");
            if (primary == 1 && !catalog.Usages.Any(usage =>
                string.Equals(usage.StepId, step.StepId, StringComparison.Ordinal)
                && string.Equals(usage.Purpose, "Primary", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"V5 step '{step.StepId}' has primary validation without a primary target.");

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

        return new GuideDefinition(catalog.GuideId, steps, catalog, additional);
    }
}
