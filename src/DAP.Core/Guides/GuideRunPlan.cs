using System.Text.RegularExpressions;
using DAP.Core.Targets;

namespace DAP.Core.Guides;

/// <summary>Runtime-neutral guide sequencing and captured-value substitution.</summary>
public sealed class GuideRunPlan
{
    private static readonly Regex CaptureToken = new(
        @"\{\{step:(?<step>[^}:]+):capture\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly GuideStep[] _steps;
    public IReadOnlyList<GuideStep> Steps => _steps;
    public int StartIndex { get; }
    public Dictionary<string, string> Captures { get; }

    public GuideRunPlan(
        IReadOnlyList<GuideStep> steps,
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null)
    {
        _steps = steps.OrderBy(step => step.Order).ToArray();
        StartIndex = startStepOrder is null
            ? 0
            : Array.FindIndex(_steps, step => step.Order == startStepOrder.Value);
        if (StartIndex < 0)
            throw new InvalidOperationException($"Guide does not contain Step order {startStepOrder}.");

        Captures = initialCapturedValues is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(initialCapturedValues, StringComparer.Ordinal);
    }

    public int NextEnabledIndex(int currentIndex) =>
        Array.FindIndex(_steps, currentIndex + 1, step => step.IsEnabled);

    public GuideStep Materialize(GuideStep step)
    {
        if (step.Target is null)
            return step;

        string Replace(string value) => CaptureToken.Replace(value, match =>
        {
            var source = match.Groups["step"].Value;
            if (!Captures.TryGetValue(source, out var captured))
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' references uncaptured runtime value from Step '{source}'.");
            return captured;
        });

        Locator MaterializeLocator(Locator locator) =>
            locator with { Value = Replace(locator.Value) };

        return step with
        {
            Target = step.Target with
            {
                Locator = MaterializeLocator(step.Target.Locator),
                Anchors = step.Target.Anchors.Select(anchor =>
                    anchor with { Locator = MaterializeLocator(anchor.Locator) }).ToArray()
            }
        };
    }

    public GuideStep? TryMaterialize(GuideStep step)
    {
        try { return Materialize(step); }
        catch (InvalidOperationException) { return null; }
    }
}
