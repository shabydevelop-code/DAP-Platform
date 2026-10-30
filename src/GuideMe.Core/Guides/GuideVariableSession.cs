using GuideMe.Core.Targets;
using System.Text.RegularExpressions;

namespace GuideMe.Core.Guides;

/// <summary>
/// One variable store per learner execution. This is the only variable model
/// intended for the unified V5 execution path after the one-time data conversion.
/// The active unified V5 learner uses this session for Web and Windows.
/// </summary>
public sealed class GuideVariableSession
{
    private static readonly Regex Reference = new(
        @"\{\{var:(?<id>[^{}:]+)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly object DiagnosticGate = new();
    private static readonly bool DiagnosticsEnabled =
        string.Equals(Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS"),
            "1", StringComparison.Ordinal);
    private static readonly string DiagnosticFile =
        Path.Combine(Path.GetTempPath(), "GuideMe-variable-diagnostic.log");

    private static void Trace(string eventName, string detail)
    {
        if (!DiagnosticsEnabled) return;
        lock (DiagnosticGate)
        {
            try
            {
                File.AppendAllText(DiagnosticFile,
                    $"{DateTimeOffset.UtcNow:O} event={eventName} {detail}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Quoted(string? value)
        => System.Text.Json.JsonSerializer.Serialize(value is { Length: > 2048 }
            ? value[..2048] + "...(truncated)" : value);

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
        Trace("variable-action",
            $"id={Quoted(action.Id)} kind={action.Kind} variable={Quoted(action.VariableId)} value={Quoted(Read(action.VariableId))}");
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
    // CSS locators can contain JSON-quoted semantic text (e.g. :cell-text or
    // :exact-text). Resolve variables inside those literals before serializing
    // them again, so quotes and backslashes in user data cannot break selectors.
    private static readonly Regex CssLocatorParts = new(
        @"(?<quoted>""(?:\\.|[^""\\])*"")|(?<reference>\{\{var:[^{}:]+\}\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string ResolveLocator(Locator locator)
    {
        if (!string.Equals(locator.Strategy, "css", StringComparison.OrdinalIgnoreCase))
            return Resolve(locator.Value);
        // One pass only: a resolved value containing {{var:...}} is literal data,
        // not another variable expression to expand.
        return CssLocatorParts.Replace(locator.Value, match =>
        {
            if (match.Groups["reference"].Success) return Resolve(match.Value);
            if (!Reference.IsMatch(match.Value)) return match.Value;
            string literal;
            try
            {
                literal = System.Text.Json.JsonSerializer.Deserialize<string>(match.Value)
                    ?? throw new InvalidOperationException("Null CSS locator text.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException("Invalid quoted CSS locator template.", ex);
            }
            return System.Text.Json.JsonSerializer.Serialize(Resolve(literal));
        });
    }

    /// <summary>Resolve variable references in all target locators, including
    /// anchors, iframe paths and additional completion conditions.</summary>
    public GuideStep Materialize(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Target is not null &&
            (step.Target.Locator.Value.Contains("{{var:", StringComparison.Ordinal)
             || step.Target.Anchors.Any(x => x.Locator.Value.Contains("{{var:", StringComparison.Ordinal))))
            Trace("materialize-input",
                $"step={Quoted(step.Id)} locator={Quoted(step.Target.Locator.Value)}");
        TargetDescriptor Expand(TargetDescriptor target) => target with
        {
            Locator = target.Locator with { Value = ResolveLocator(target.Locator) },
            Anchors = target.Anchors.Select(anchor => anchor with
            {
                Locator = anchor.Locator with { Value = ResolveLocator(anchor.Locator) }
            }).ToArray(),
            FrameContext = target.FrameContext is null ? null : target.FrameContext with
            {
                Path = target.FrameContext.Path.Select(locator =>
                    locator with { Value = ResolveLocator(locator) }).ToArray()
            }
        };
        var expanded = step with
        {
            Bubble = step.Bubble with { Content = Resolve(step.Bubble.Content) },
            Target = step.Target is null ? null : Expand(step.Target),
            Validation = step.Validation is null ? null : step.Validation with
            {
                ExpectedValue = step.Validation.ExpectedValue is null
                    ? null : Resolve(step.Validation.ExpectedValue)
            },
            CompletionConditions = step.CompletionConditions?.Select(condition =>
                condition with
                {
                    Target = Expand(condition.Target),
                    ExpectedValue = condition.ExpectedValue is null
                        ? null : Resolve(condition.ExpectedValue)
                }).ToArray(),
            VariableActions = step.VariableActions?.Select(action =>
                action.SourceTarget is null ? action :
                action with { SourceTarget = Expand(action.SourceTarget) }).ToArray()
        };
        if (expanded.Target is not null &&
            (step.Target?.Locator.Value.Contains("{{var:", StringComparison.Ordinal) == true))
            Trace("materialize-result",
                $"step={Quoted(step.Id)} locator={Quoted(expanded.Target.Locator.Value)}");
        return expanded;
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
            Trace("variable-observation",
                $"id={Quoted(action.Id)} variable={Quoted(action.VariableId)} timing={timing} observed={Quoted(value)}");
            if (value is null)
                throw new InvalidOperationException(
                    $"Variable action '{action.Id}' could not read its target.");
            Execute(action, value);
        }
    }

}
