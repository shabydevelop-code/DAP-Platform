namespace DAP.Core.Guides;

/// <summary>Runtime-neutral completion checks over adapter observations.</summary>
public static class GuideCompletionPolicy
{
    public static bool IsSupportedObservationKind(string kind) => kind.Trim().ToLowerInvariant() is
        "target-exists" or "target-not-exists" or "target-enabled" or "value-equals";

    public static bool IsSatisfied(StepCompletionCondition condition, bool resolved, bool ambiguous, bool enabled = false, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition.Kind.Trim().ToLowerInvariant() switch
        {
            "target-exists" => resolved,
            "target-not-exists" => !resolved && !ambiguous,
            "target-enabled" => resolved && enabled,
            "value-equals" => resolved && condition.ExpectedValue is not null
                && GuideValidationPolicy.IsValueSatisfied(new ValidationDefinition("value-equals", condition.ExpectedValue), value),
            _ => throw new NotSupportedException($"Unsupported completion condition kind '{condition.Kind}'.")
        };
    }
}
