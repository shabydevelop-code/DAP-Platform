using DAP.Core.Targets;

namespace DAP.Core.Guides;

public enum StepAdvanceMode
{
    AutomaticOnValidation,
    Manual
}

public sealed record GuideStep(
    string Id,
    int Order,
    TargetDescriptor? Target,
    BubbleDefinition Bubble,
    ValidationDefinition? Validation = null,
    StepAdvanceMode AdvanceMode = StepAdvanceMode.AutomaticOnValidation,
    StepContextDefinition? Context = null,
    StepCaptureDefinition? Capture = null,
    IReadOnlyList<StepCompletionCondition>? CompletionConditions = null,
    bool IsEnabled = true,
    string? AutomationValue = null,
    string? ApplicationContextKey = null);

/// <summary>Optional capture schedule. Null preserves existing runtime behavior.</summary>
public enum StepCaptureTiming
{
    BeforeAction,
    DuringStep,
    AfterAction
}

public sealed record StepCaptureDefinition(
    TargetRuntime Runtime,
    Locator Locator,
    string Property,
    string? Pattern = null,
    StepCaptureTiming? Timing = null);

public sealed record StepCompletionCondition(
    string Kind,
    TargetDescriptor Target,
    string? ExpectedValue = null);
