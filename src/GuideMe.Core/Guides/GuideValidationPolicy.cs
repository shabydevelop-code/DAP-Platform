namespace GuideMe.Core.Guides;

/// <summary>Runtime-neutral evaluation of persisted value validation.</summary>
public static class GuideValidationPolicy
{
    public static bool IsValueValidation(ValidationDefinition validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        return validation.Kind.Trim().Equals("value-equals", StringComparison.OrdinalIgnoreCase)
            || validation.Kind.Trim().Equals("value-not-empty", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? NormalizeLineEndings(string? value) => value?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static bool IsValueSatisfied(ValidationDefinition validation, string? observedValue)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var kind = validation.Kind.Trim();
        if (kind.Equals("value-equals", StringComparison.OrdinalIgnoreCase))
            return string.Equals(NormalizeLineEndings(observedValue), NormalizeLineEndings(validation.ExpectedValue ?? string.Empty), StringComparison.Ordinal);
        if (kind.Equals("value-not-empty", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(observedValue);
        throw new NotSupportedException($"Unsupported value validation kind '{validation.Kind}'.");
    }
}
