using GuideMe.Core.Targets;
using System.Text.RegularExpressions;

namespace GuideMe.Core.Guides;

/// <summary>
/// One variable store per learner execution. This is the only variable model
/// intended for the unified V5 execution path after the one-time data conversion.
/// Not wired into production execution until the old capture path is removed.
/// </summary>
public sealed class GuideVariableSession
{
    private static readonly Regex Reference = new(
        @"\{\{var:(?<id>[^{}:]+)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, string?> _values;

    public GuideVariableSession(IReadOnlyList<GuideVariableDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var variable in definitions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variable.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(variable.DisplayName);
            if (!_values.TryAdd(variable.Id, variable.InitialValue))
                throw new InvalidOperationException($"Duplicate guide variable '{variable.Id}'.");
        }
    }

    public string? Read(string variableId) =>
        _values.TryGetValue(variableId, out var value) ? value :
        throw new InvalidOperationException($"Unknown guide variable '{variableId}'.");

    public void Set(string variableId, string? value)
    {
        if (!_values.ContainsKey(variableId))
            throw new InvalidOperationException($"Unknown guide variable '{variableId}'.");
        _values[variableId] = value;
    }

    public void Execute(GuideVariableAction action, string? observedValue = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.Kind)
        {
            case GuideVariableActionKind.SetValue:
                Set(action.VariableId, action.Value ??
                    throw new InvalidOperationException("SetValue requires a value."));
                break;
            case GuideVariableActionKind.Clear:
                Set(action.VariableId, null);
                break;
            case GuideVariableActionKind.CopyFromVariable:
                Set(action.VariableId, Read(action.SourceVariableId ??
                    throw new InvalidOperationException("CopyFromVariable requires a source.")));
                break;
            case GuideVariableActionKind.ReadFromTarget:
                Set(action.VariableId, observedValue ??
                    throw new InvalidOperationException(
                        $"Target value for '{action.VariableId}' was not observed."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action.Kind));
        }
    }

    public string Resolve(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Reference.Replace(expression, match =>
        {
            var id = match.Groups["id"].Value;
            return Read(id) ?? throw new InvalidOperationException(
                $"Guide variable '{id}' has not been assigned.");
        });
    }
    /// <summary>Resolve variable references in all target locators, including
    /// anchors, iframe paths and additional completion conditions.</summary>
    public GuideStep Materialize(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        TargetDescriptor Expand(TargetDescriptor target) => target with
        {
            Locator = target.Locator with { Value = Resolve(target.Locator.Value) },
            Anchors = target.Anchors.Select(anchor => anchor with
            {
                Locator = anchor.Locator with { Value = Resolve(anchor.Locator.Value) }
            }).ToArray(),
            FrameContext = target.FrameContext is null ? null : target.FrameContext with
            {
                Path = target.FrameContext.Path.Select(locator =>
                    locator with { Value = Resolve(locator.Value) }).ToArray()
            }
        };
        return step with
        {
            Target = step.Target is null ? null : Expand(step.Target),
            CompletionConditions = step.CompletionConditions?.Select(condition =>
                condition with { Target = Expand(condition.Target) }).ToArray(),
            VariableActions = step.VariableActions?.Select(action =>
                action.SourceTarget is null ? action :
                action with { SourceTarget = Expand(action.SourceTarget) }).ToArray()
        };
    }

    /// <summary>
    /// Run ordered actions at a step boundary. Only target reads are delegated to
    /// the active Web/Windows adapter; all assignments remain platform neutral.
    /// </summary>
    public async Task ExecuteActionsAsync(
        IReadOnlyList<GuideVariableAction> actions,
        StepCaptureTiming timing,
        Func<GuideVariableAction, CancellationToken, Task<string?>> readTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(readTarget);
        foreach (var action in actions.OrderBy(x => x.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action.Timing != timing) continue;
            if (action.Kind != GuideVariableActionKind.ReadFromTarget)
            {
                Execute(action);
                continue;
            }
            var value = await readTarget(action, cancellationToken);
            if (value is null)
                throw new InvalidOperationException(
                    $"Variable action '{action.Id}' could not read its target.");
            Execute(action, value);
        }
    }

}
