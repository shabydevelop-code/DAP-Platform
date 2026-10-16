using System.Text.Json;
using DAP.Core.Guides;
using DAP.Core.Targets;
using Microsoft.Data.Sqlite;

namespace DAP.Data.Sqlite.Guides;

/// <summary>
/// Experimental compiler from V5 definitions to the existing platform-neutral
/// execution contract. Not wired into production launch.
/// </summary>
public static class GuideV5ExecutionCompiler
{
    public sealed record ExecutionPlan(IReadOnlyList<GuideStep> Steps,
        IReadOnlyList<GuideApplicationContext> Contexts);

    public static async Task<ExecutionPlan> CompileAsync(
        string databasePath, string guideKey, CancellationToken cancellationToken = default)
    {
        var data = await GuideV5GuideReader.ReadAsync(databasePath, guideKey, cancellationToken);
        var uses = data.ElementCatalog.Usages.ToDictionary(x => (x.StepId, x.UsageId));
        TargetDescriptor Target(string stepId, string usageId)
        {
            if (!uses.TryGetValue((stepId, usageId), out var use)
                || !data.ElementCatalog.Elements.TryGetValue(use.ElementId, out var element))
                throw new InvalidOperationException($"Missing V5 element usage '{stepId}/{usageId}'.");
            if (element.Locators.Count != 1)
                throw new NotSupportedException($"V5 element '{element.ElementId}' must have one locator for current runtime.");
            if (!Enum.TryParse<TargetRuntime>(element.Runtime, out var runtime))
                throw new InvalidOperationException($"Invalid runtime of element '{element.ElementId}'.");
            var anchors = element.Anchors.OrderBy(x => x.Order).Select(relation =>
            {
                if (!data.ElementCatalog.Elements.TryGetValue(relation.AnchorElementId, out var anchor)
                    || anchor.Locators.Count != 1)
                    throw new InvalidOperationException($"Invalid anchor '{relation.AnchorElementId}'.");
                if (!Enum.TryParse<AnchorRelation>(relation.Relation, out var kind))
                    throw new InvalidOperationException($"Invalid relation '{relation.Relation}'.");
                var l = anchor.Locators[0];
                return new Anchor(new Locator(l.Strategy, l.Value), kind);
            }).ToArray();
            FrameContext? frame = null;
            if (element.FrameContextJson is not null)
            {
                var path = JsonSerializer.Deserialize<Locator[]>(element.FrameContextJson)
                    ?? throw new InvalidOperationException($"Invalid frame context '{element.ElementId}'.");
                frame = new FrameContext(path);
            }
            var main = element.Locators[0];
            return TargetDescriptor.Create(runtime, new Locator(main.Strategy, main.Value), anchors, frame);
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        var steps = new List<GuideStep>();
        foreach (var s in data.Steps)
        {
            string bubbleContent;
            BubblePlacement placement;
            string? appContext;
            StepContextDefinition? context;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT b.Content,b.Placement,s.ApplicationContextId,s.ContextKind,s.ContextValue
                    FROM GuideSteps s JOIN StepBubbles b
                    ON b.GuideId=s.GuideId AND b.StepId=s.StepId
                    WHERE s.GuideId=$g AND s.StepId=$s
                    """;
                cmd.Parameters.AddWithValue("$g", s.GuideId);
                cmd.Parameters.AddWithValue("$s", s.StepId);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Missing bubble for V5 step '{s.StepId}'.");
                bubbleContent = reader.GetString(0);
                if (!Enum.TryParse(reader.GetString(1), out placement))
                    throw new InvalidOperationException($"Invalid bubble placement for '{s.StepId}'.");
                appContext = reader.IsDBNull(2) ? null : reader.GetString(2);
                context = reader.IsDBNull(3) ? null :
                    new StepContextDefinition(reader.GetString(3), reader.GetString(4));
            }

            var primaryUses = data.ElementCatalog.Usages.Where(u =>
                u.StepId == s.StepId && u.Purpose == "Primary").ToArray();
            if (primaryUses.Length > 1)
                throw new InvalidOperationException($"Multiple primary targets at '{s.StepId}'.");
            var mainTarget = primaryUses.Length == 0 ? null : Target(s.StepId, primaryUses[0].UsageId);
            var primarySignals = s.Signals.Where(x => x.Role == "PrimaryValidation").ToArray();
            if (primarySignals.Length > 1)
                throw new InvalidOperationException($"Multiple primary validations at '{s.StepId}'.");
            ValidationDefinition? validation = null;
            if (primarySignals.Length == 1)
            {
                var v = primarySignals[0];
                if (v.TargetUsageId != null && (primaryUses.Length != 1 || v.TargetUsageId != primaryUses[0].UsageId))
                    throw new NotSupportedException($"Primary validation for '{s.StepId}' refers to a different target.");
                validation = new ValidationDefinition(v.Kind, v.ExpectedValue,
                    v.OptionsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string,string>>(v.OptionsJson));
            }
            var completion = s.Signals.Where(x => x.Role == "AdditionalCompletionCondition")
                .Select(x => new StepCompletionCondition(x.Kind,
                    Target(s.StepId, x.TargetUsageId ?? throw new InvalidOperationException(
                        $"Completion signal '{x.SignalId}' has no target.")), x.ExpectedValue)).ToArray();
            var captures = data.ContextsAndCaptures.Captures.Where(x => x.StepId == s.StepId).ToArray();
            if (captures.Length > 1)
                throw new NotSupportedException($"Multiple captures at '{s.StepId}' are not supported by the current runtime.");
            StepCaptureDefinition? capture = null;
            if (captures.Length == 1)
            {
                var c = captures[0];
                if (c.VariableId != s.StepId)
                    throw new NotSupportedException($"Capture '{c.CaptureId}' uses a V5 variable that cannot be mapped to the legacy step token.");
                var t = Target(s.StepId, c.TargetUsageId);
                StepCaptureTiming? timing = null;
                if (c.Timing is not null)
                {
                    if (!Enum.TryParse<StepCaptureTiming>(c.Timing, out var parsed))
                        throw new InvalidOperationException($"Invalid capture timing '{c.Timing}'.");
                    timing = parsed;
                }
                capture = new StepCaptureDefinition(t.Runtime, t.Locator, c.Property, c.Pattern, timing);
            }
            if (!Enum.TryParse<StepAdvanceMode>(s.AdvancePolicy, out var policy))
                throw new InvalidOperationException($"Invalid advance policy for '{s.StepId}'.");
            steps.Add(new GuideStep(s.StepId, s.Order, mainTarget,
                new BubbleDefinition(bubbleContent, placement), validation, policy,
                context, capture, completion, s.IsEnabled, appContext));
        }

        var contexts = data.ContextsAndCaptures.Contexts.Select(c =>
        {
            if (!Enum.TryParse<TargetRuntime>(c.Runtime, out var runtime))
                throw new InvalidOperationException($"Invalid runtime '{c.Runtime}'.");
            return new GuideApplicationContext(c.ContextId, runtime,
                c.Matchers.Select(m => new ApplicationContextMatcher(m.Order, m.Kind, m.Value)).ToArray());
        }).ToArray();
        return new ExecutionPlan(steps, contexts);
    }
}
