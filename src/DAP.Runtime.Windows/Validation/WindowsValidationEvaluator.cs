using System.Windows.Automation;
using DAP.Core.Guides;

namespace DAP.Runtime.Windows.Validation;

public sealed class WindowsValidationEvaluator
{
    public bool IsSatisfied(AutomationElement target, ValidationDefinition validation)
    {
        var kind = validation.Kind.Trim().ToLowerInvariant();
        if (kind == "value-equals")
        {
            if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                return false;

            return string.Equals(
                ((ValuePattern)pattern).Current.Value,
                validation.ExpectedValue ?? string.Empty,
                StringComparison.Ordinal);
        }

        if (kind == "value-not-empty")
        {
            if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                return false;

            return !string.IsNullOrWhiteSpace(((ValuePattern)pattern).Current.Value);
        }

        throw new NotSupportedException($"Unsupported Windows validation kind '{validation.Kind}'.");
    }
}
