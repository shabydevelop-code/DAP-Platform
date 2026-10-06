using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.Runtime.Web.Browser;

/// <summary>
/// Browser boundary for the Web learner. Guide sequencing, validation policy,
/// context, capture and reconciliation remain owned by DAP Runtime.
/// The production implementation uses the browser extension. The contract keeps
/// browser mechanics isolated from learner policy and test automation.
/// </summary>
public interface IWebBrowserAdapter
{
    Task<WebTargetResolution> ResolveTargetAsync(TargetDescriptor descriptor, CancellationToken cancellationToken = default);
    Task<bool> IsContextActiveAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task<bool> IsStableForPresentationAsync(GuideStep step, TimeSpan quietWindow, CancellationToken cancellationToken = default);
    Task ArmValidationAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task<WebValidationCommit?> WaitForValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task<WebValidationCommit?> WaitForArmedValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task ConsumeValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task<bool> IsPrimaryValidationSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task<bool> AreCompletionConditionsSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default);
    Task<WebBubblePresentation> EnsureBubbleShownAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default);
    Task WaitForPresentationInvalidationAsync(CancellationToken cancellationToken = default);
    Task HideBubbleAsync(CancellationToken cancellationToken = default);
    Task WaitForCenteredStepDismissalAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default);
    Task WaitForGuideCompletedDismissalAsync(CancellationToken cancellationToken = default);
    Task<string?> CaptureAsync(GuideStep step, CancellationToken cancellationToken = default);
}

public sealed record WebTargetResolution(WebTargetResolutionStatus Status, int MatchCount = 0);
public enum WebTargetResolutionStatus { Resolved, NotFound, Ambiguous }
public sealed record WebValidationCommit(string StepId, string Kind);
public sealed record WebBubblePresentation(WebTargetResolutionStatus Status, int MatchCount = 0);
