namespace GuideMe.Core.Guides;

/// <summary>Runtime-neutral completion checks over adapter observations.</summary>
public static class GuideCompletionPolicy
{
    // Ignore presentation-only whitespace for substring completion checks.
    // Exact value validation intentionally retains its existing semantics.
    private static bool ContainsIgnoringWhitespace(string? actual, string expected)
    {
        if (actual is null) return false;
        static string Compact(string text) => new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var needle = Compact(expected);
        return needle.Length > 0 && Compact(actual).Contains(needle, StringComparison.Ordinal);
    }

    public static bool IsSupportedObservationKind(string kind) => kind.Trim().ToLowerInvariant() is
        "target-exists" or "target-not-exists" or "target-appeared" or "target-enabled" or "value-equals" or "value-contains";

    public static bool IsSatisfied(StepCompletionCondition condition, bool resolved, bool ambiguous, bool enabled = false, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition.Kind.Trim().ToLowerInvariant() switch
        {
            "target-exists" or "target-appeared" => resolved,
            "target-not-exists" => !resolved && !ambiguous,
            "target-enabled" => resolved && enabled,
            "value-contains" => resolved && !string.IsNullOrEmpty(condition.ExpectedValue) &&
                ContainsIgnoringWhitespace(value, condition.ExpectedValue),
            "value-equals" => resolved && condition.ExpectedValue is not null
                && GuideValidationPolicy.IsValueSatisfied(new ValidationDefinition("value-equals", condition.ExpectedValue), value),
            _ => throw new NotSupportedException($"Unsupported completion condition kind '{condition.Kind}'.")
        };
    }
}
