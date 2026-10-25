using GuideMe.Core.Targets;

namespace GuideMe.Core.Guides;

/// <summary>
/// Author-defined variable. The definition belongs to a guide; its mutable value
/// belongs only to an individual learner run, never to the V5 database.
/// </summary>
public sealed record GuideVariableDefinition(
    string Id,
    string DisplayName,
    string? InitialValue = null);

public enum GuideVariableActionKind
{
    SetValue,
    ReadFromTarget,
    Clear,
    CopyFromVariable
}

/// <summary>
/// Ordered step action against a guide-level variable. ReadFromTarget reuses
/// the step target catalog and the existing Web/Windows target adapters.
/// </summary>
public sealed record GuideVariableAction(
    string Id,
    string VariableId,
    GuideVariableActionKind Kind,
    int Order,
    string? Value = null,
    string? SourceVariableId = null,
    string? TargetUsageId = null,
    string? Property = null,
    string? Pattern = null,
    StepCaptureTiming Timing = StepCaptureTiming.AfterAction,
    TargetDescriptor? SourceTarget = null);
