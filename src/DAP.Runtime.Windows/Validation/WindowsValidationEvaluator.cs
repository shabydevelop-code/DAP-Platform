using System.Windows.Automation;
using DAP.Core.Guides;

namespace DAP.Runtime.Windows.Validation;

public sealed class WindowsValidationEvaluator
{
    public bool IsSatisfied(AutomationElement target, ValidationDefinition validation)
    {
        if (!GuideValidationPolicy.IsValueValidation(validation))
            throw new NotSupportedException($"Unsupported Windows validation kind '{validation.Kind}'.");

        if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            return false;

        return GuideValidationPolicy.IsValueSatisfied(validation, ((ValuePattern)pattern).Current.Value);
    }
}
