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

    // Compare authoring text without incidental leading/trailing whitespace on each line.
    // Keep internal spaces and line boundaries significant; do not alter stored values.
    internal static string? NormalizeExpectedText(string? value)
    {
        if (value is null) return null;
        var lines = NormalizeLineEndings(value)!.Split('\n')
            .Select(line => line.Trim())
            .ToArray();
        var first = 0;
        var last = lines.Length - 1;
        while (first <= last && lines[first].Length == 0) first++;
        while (last >= first && lines[last].Length == 0) last--;
        return first > last ? string.Empty : string.Join("\n", lines[first..(last + 1)]);
    }

    internal static string? NormalizeLineEndings(string? value) => value?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static bool IsValueSatisfied(ValidationDefinition validation, string? observedValue)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var kind = validation.Kind.Trim();
        if (kind.Equals("value-equals", StringComparison.OrdinalIgnoreCase))
            return string.Equals(NormalizeExpectedText(observedValue), NormalizeExpectedText(validation.ExpectedValue ?? string.Empty), StringComparison.Ordinal);
        if (kind.Equals("value-not-empty", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(observedValue);
        throw new NotSupportedException($"Unsupported value validation kind '{validation.Kind}'.");
    }
}
