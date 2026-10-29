namespace GuideMe.Core.Guides;

/// <summary>Runtime-neutral completion checks over adapter observations.</summary>
public static class GuideCompletionPolicy
{
    public static bool IsSupportedObservationKind(string kind) => kind.Trim().ToLowerInvariant() is
        "target-exists" or "target-not-exists" or "target-appeared" or "target-vanished" or "target-enabled" or "value-equals" or "value-contains";

    public static bool IsSatisfied(StepCompletionCondition condition, bool resolved, bool ambiguous, bool enabled = false, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition.Kind.Trim().ToLowerInvariant() switch
        {
            "target-exists" or "target-appeared" => resolved,
            "target-not-exists" or "target-vanished" => !resolved && !ambiguous,
            "target-enabled" => resolved && enabled,
            "value-contains" => resolved && !string.IsNullOrEmpty(condition.ExpectedValue) &&
                value?.Contains(condition.ExpectedValue, StringComparison.Ordinal) == true,
            "value-equals" => resolved && condition.ExpectedValue is not null
                && GuideValidationPolicy.IsValueSatisfied(new ValidationDefinition("value-equals", condition.ExpectedValue), value),
            _ => throw new NotSupportedException($"Unsupported completion condition kind '{condition.Kind}'.")
        };
    }
}
