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
    StepCaptureDefinition? Capture = null);

public sealed record StepCaptureDefinition(
    TargetRuntime Runtime,
    Locator Locator,
    string Property,
    string? Pattern = null);
