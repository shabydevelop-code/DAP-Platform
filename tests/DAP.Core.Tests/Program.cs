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

Console.WriteLine("All production shared-runtime checks passed.");
