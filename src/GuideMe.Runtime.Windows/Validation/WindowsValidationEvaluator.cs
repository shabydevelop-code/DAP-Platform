using System.Windows.Automation;
using GuideMe.Core.Guides;

namespace GuideMe.Runtime.Windows.Validation;

public sealed class WindowsValidationEvaluator
{
    public bool IsSatisfied(AutomationElement target, ValidationDefinition validation)
    {
        if (!GuideValidationPolicy.IsValueValidation(validation))
            throw new NotSupportedException($"Unsupported Windows validation kind '{validation.Kind}'.");

        if (target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            return GuideValidationPolicy.IsValueSatisfied(validation, ((ValuePattern)pattern).Current.Value);

        // Document editors such as Notepad expose their content through TextPattern.
        // Read it only when the normal step validation is requested.
        if (target.Current.ControlType == ControlType.Document &&
            target.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern))
            return GuideValidationPolicy.IsValueSatisfied(validation,
                ((TextPattern)textPattern).DocumentRange.GetText(-1));

        return false;
    }
}
