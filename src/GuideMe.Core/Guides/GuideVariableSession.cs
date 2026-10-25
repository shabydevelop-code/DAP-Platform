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
}
