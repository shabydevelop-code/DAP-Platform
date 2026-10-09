using DAP.Core.Guides;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}

var step = new GuideStep("live", 1, null, new BubbleDefinition("Test"), new ValidationDefinition("clicked"));
var state = new GuideActiveStepState(step);
var engine = new UnifiedGuideStepEngine();

Check(state.ObserveContext(false) == GuideStepReconciliationResult.WaitingForContext, "Shared runtime waits for context");
Check(state.ObserveTarget(false) == GuideStepReconciliationResult.WaitingForTarget, "Shared runtime waits for target");
Check(engine.EvaluateCompletion(state, true, false) == GuideStepReconciliationResult.WaitingForValidation, "Shared runtime waits for conditions");
Check(engine.EvaluateCompletion(state, true, true) == GuideStepReconciliationResult.Completed, "Shared runtime completes validated step");
Check(state.TrySignalReady() && !state.TrySignalReady(), "Shared runtime signals readiness once");

// Shared readiness must invalidate stale presentation after target loss.
var readinessState = new GuideActiveStepState(step);
readinessState.MarkPresentationReady();
Check(engine.ObserveReadiness(readinessState, true, false, false)
    == GuideStepReconciliationResult.WaitingForTarget,
    "Shared engine waits when target is missing");
Check(!readinessState.PresentationReady,
    "Missing target invalidates presentation");
readinessState.MarkPresentationReady();
Check(engine.ObserveReadiness(readinessState, false, true, true)
    == GuideStepReconciliationResult.WaitingForContext,
    "Inactive context takes precedence over a visible target");
Check(!readinessState.PresentationReady,
    "Inactive context invalidates presentation");
Check(engine.ObserveReadiness(readinessState, true, true, true)
    == GuideStepReconciliationResult.WaitingForAction,
    "Shared engine accepts a visible target in active context");

Check(GuideStepExecutionPolicy.ShouldConsumeInvalidCommit(step, true, false) == false,
    "Click commits are not consumed as invalid value edits");
var valueStep = step with { Validation = new ValidationDefinition("value-not-empty") };
Check(GuideStepExecutionPolicy.ShouldConsumeInvalidCommit(valueStep, true, false),
    "Invalid value commits require another learner action");
Check(!GuideStepExecutionPolicy.ShouldConsumeInvalidCommit(valueStep, true, true),
    "Valid value commits are preserved while completion conditions settle");
Check(!GuideStepExecutionPolicy.ShouldConsumeInvalidCommit(valueStep, false, false),
    "No commit cannot be consumed");

var captures = new Dictionary<string, string>(StringComparer.Ordinal);
Check(!GuideRunPlan.RecordCapture(captures, "step-1", null) && captures.Count == 0,
    "Missing capture leaves shared state unchanged");
Check(GuideRunPlan.RecordCapture(captures, "step-1", "123") && captures["step-1"] == "123",
    "Shared capture records first observed value");
Check(!GuideRunPlan.RecordCapture(captures, "step-1", "123"),
    "Shared capture ignores unchanged value");
Check(GuideRunPlan.RecordCapture(captures, "step-1", "456") && captures["step-1"] == "456",
    "Shared capture updates changed value");

var legacyCapture = step with { Capture = new StepCaptureDefinition(DAP.Core.Targets.TargetRuntime.Web, null!, "value") };
Check(GuideRunPlan.ShouldCapture(legacyCapture, DAP.Core.Targets.TargetRuntime.Web, StepCaptureTiming.BeforeAction),
    "Legacy Web capture remains before action");
Check(!GuideRunPlan.ShouldCapture(legacyCapture, DAP.Core.Targets.TargetRuntime.Web, StepCaptureTiming.AfterAction),
    "Legacy Web capture does not run after action");
Check(GuideRunPlan.ShouldCapture(legacyCapture, DAP.Core.Targets.TargetRuntime.Windows, StepCaptureTiming.DuringStep),
    "Legacy Windows capture remains available during step");
Check(GuideRunPlan.ShouldCapture(legacyCapture, DAP.Core.Targets.TargetRuntime.Windows, StepCaptureTiming.AfterAction),
    "Legacy Windows capture remains available after action");
var explicitCapture = legacyCapture with { Capture = legacyCapture.Capture! with { Timing = StepCaptureTiming.AfterAction } };
Check(GuideRunPlan.ShouldCapture(explicitCapture, DAP.Core.Targets.TargetRuntime.Web, StepCaptureTiming.AfterAction),
    "Explicit after-action timing applies to Web");
Check(GuideRunPlan.ShouldCapture(explicitCapture, DAP.Core.Targets.TargetRuntime.Windows, StepCaptureTiming.AfterAction),
    "Explicit after-action timing applies to Windows");
Check(!GuideRunPlan.ShouldCapture(explicitCapture, DAP.Core.Targets.TargetRuntime.Windows, StepCaptureTiming.DuringStep),
    "Explicit timing overrides legacy Windows during-step capture");

Console.WriteLine("All production shared-runtime checks passed.");
