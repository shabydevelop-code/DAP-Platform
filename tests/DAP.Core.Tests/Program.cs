using DAP.Core.Guides;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}

static UnifiedStepStateMachine Machine(string kind = "clicked")
    => new(new GuideStep("test", 1, null, new BubbleDefinition("Test"),
        new ValidationDefinition(kind)));

static UnifiedStepObservation Observation(
    bool context = true, bool target = true, bool visible = true,
    bool stable = true, bool primary = false, bool conditions = false,
    bool action = false)
    => new(context, target, visible, stable, primary, conditions, ActionObserved: action);

var machine = Machine();
var missing = machine.Advance(Observation(context: false));
Check(missing.Status == GuideStepReconciliationResult.WaitingForContext, "Inactive context waits");
var show = machine.Advance(Observation());
Check(show.PresentationAction == UnifiedPresentationAction.Show, "Ready target requests bubble");
machine.AcknowledgePresentation(show.PresentationAction);
Check(machine.Advance(Observation()).PresentationAction == UnifiedPresentationAction.None, "No repeated show");
var hide = machine.Advance(Observation(target: false));
Check(hide.PresentationAction == UnifiedPresentationAction.Hide, "Lost target requests hide");
machine.AcknowledgePresentation(hide.PresentationAction);
Check(machine.Advance(Observation(action: true)).Status == GuideStepReconciliationResult.WaitingForValidation, "Action awaits conditions");
Check(machine.Advance(Observation(context: false, target: false, conditions: true)).StepCompleted, "Latched action completes after navigation");
Check(machine.Advance(Observation()).StepCompleted, "Completed state remains completed");

var disappearance = Machine("target-disappeared");
Check(!disappearance.Advance(Observation()).StepCompleted, "Disappearance needs prior target");
Check(disappearance.Advance(Observation(target: false, conditions: true)).StepCompleted, "Disappearance completes after target loss");

var unstable = Machine();
Check(unstable.Advance(Observation(stable: false)).Status == GuideStepReconciliationResult.WaitingForTarget, "Unstable presentation waits");

var running = Machine();
var effects = new List<UnifiedPresentationAction>();
var observed = 0;
await running.RunAsync(
    _ => Task.FromResult(++observed == 1 ? Observation() : Observation(primary: true, conditions: true)),
    (action, _) => { effects.Add(action); return Task.CompletedTask; },
    (_, _) => Task.CompletedTask,
    CancellationToken.None);
Check(effects.SequenceEqual(new[] { UnifiedPresentationAction.Show, UnifiedPresentationAction.Hide }), "Loop applies show and hide once");
Console.WriteLine("All unified state-machine checks passed.");
