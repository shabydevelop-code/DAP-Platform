namespace DAP.Core.Guides;

/// <summary>Runtime-independent invariants for presentation and hybrid step execution.</summary>
public enum GuideStepPresentationKind { CenteredInformation, TargetAttached }

public static class GuideStepExecutionPolicy
{
    public static GuideStepPresentationKind Classify(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Bubble.Placement == BubblePlacement.Center)
        {
            RequireCenteredInformationStep(step);
            return GuideStepPresentationKind.CenteredInformation;
        }
        if (step.Target is null)
            throw new InvalidOperationException($"Target-attached Guide Step '{step.Id}' must define a target.");
        return GuideStepPresentationKind.TargetAttached;
    }

    public static bool IsAutomaticValidationStep(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation && step.Validation is not null;
    }

    public static bool IsClickValidationStep(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return IsAutomaticValidationStep(step)
            && string.Equals(step.Validation!.Kind, "clicked", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTargetDisappearanceStep(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return IsAutomaticValidationStep(step)
            && string.Equals(step.Validation!.Kind, "target-disappeared", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCenteredInformationStep(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Target is null && step.Bubble.Placement == BubblePlacement.Center;
    }

    public static void RequireCenteredInformationStep(GuideStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!IsCenteredInformationStep(step)
            || step.AdvanceMode != StepAdvanceMode.Manual
            || step.Validation is not null
            || step.Context is not null
            || step.Capture is not null
            || step.CompletionConditions is { Count: > 0 })
            throw new InvalidOperationException(
                $"Centered Guide Step '{step.Id}' must be a pure Manual information Step with no target, context, validation, capture, or completion conditions.");
    }

    public static void RequireHybridValueStep(GuideStep step, string runtime)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.AdvanceMode != StepAdvanceMode.AutomaticOnValidation
            || step.Validation is null
            || !GuideValidationPolicy.IsValueValidation(step.Validation))
            throw new InvalidOperationException(
                $"{runtime} Hybrid Step '{step.Id}' requires automatic value validation.");
    }
}
