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
var hybridStep = new GuideStep("hybrid", 1, null, new BubbleDefinition("Test"),
    new ValidationDefinition("clicked"), AutomationValue: "alpha");
var hybridMachine = new UnifiedStepStateMachine(hybridStep);
Check(!hybridMachine.TryApplyHybridValue(new UnifiedStepOptions(UnifiedLearnerMode.Manual), true),
    "Manual mode never applies automation");
Check(!hybridMachine.TryApplyHybridValue(new UnifiedStepOptions(UnifiedLearnerMode.Hybrid), false),
    "Hybrid mode waits for resolved target");
Check(hybridMachine.TryApplyHybridValue(new UnifiedStepOptions(UnifiedLearnerMode.Hybrid), true),
    "Hybrid applies persisted value once");
Check(!hybridMachine.TryApplyHybridValue(new UnifiedStepOptions(UnifiedLearnerMode.Hybrid), true),
    "Hybrid never reapplies the value");
var emptyHybrid = Machine();
Check(!emptyHybrid.TryApplyHybridValue(new UnifiedStepOptions(UnifiedLearnerMode.Hybrid), true),
    "Hybrid skips steps without persisted automation value");

var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await Machine().RunAsync(
        _ => throw new Exception("Canceled run must not observe"),
        (_, _) => Task.CompletedTask,
        (_, _) => Task.CompletedTask,
        canceled.Token);
    throw new Exception("FAILED: Canceled run should throw");
}
catch (OperationCanceledException)
{
    Console.WriteLine("PASS: Canceled run stops before observation");
}

var adapter = new FakeUnifiedAdapter();
await new UnifiedStepRunner().RunAsync(hybridStep,
    new UnifiedStepOptions(UnifiedLearnerMode.Hybrid), adapter, CancellationToken.None);
Check(adapter.AutomationCalls == 1, "Shared runner applies hybrid value exactly once");
Check(adapter.PresentationActions.SequenceEqual(new[] {
    UnifiedPresentationAction.Show, UnifiedPresentationAction.Hide
}), "Shared runner delegates presentation lifecycle");

var editStep = new GuideStep("edit", 1, null, new BubbleDefinition("Edit"),
    new ValidationDefinition("value"));
var editMachine = new UnifiedStepStateMachine(editStep);
Check(!editMachine.Advance(new UnifiedStepObservation(true, true, true, true, true, false,
    IsTextEditTarget: true, TextEditCommitted: true)).StepCompleted,
    "Committed edit waits for completion conditions");
Check(editMachine.Advance(new UnifiedStepObservation(false, false, false, false, true, true,
    IsTextEditTarget: true)).StepCompleted,
    "Committed edit survives context replacement");

var failingAdapter = new FakeUnifiedAdapter { FailOnWait = true };
try
{
    await new UnifiedStepRunner().RunAsync(hybridStep,
        new UnifiedStepOptions(UnifiedLearnerMode.Manual), failingAdapter, CancellationToken.None);
    throw new Exception("FAILED: Adapter failure should propagate");
}
catch (InvalidOperationException)
{
    Check(failingAdapter.PresentationActions.Last() == UnifiedPresentationAction.Hide,
        "Adapter failure hides stale bubble");
}

var flicker = Machine();
var first = flicker.Advance(Observation());
flicker.AcknowledgePresentation(first.PresentationAction);
Check(flicker.Advance(Observation(stable: false)).PresentationAction == UnifiedPresentationAction.None,
    "Temporary instability does not flicker visible bubble");
Check(flicker.Advance(Observation()).PresentationAction == UnifiedPresentationAction.None,
    "Stable layout does not show duplicate bubble");
Check(flicker.Advance(Observation(target: false)).PresentationAction == UnifiedPresentationAction.Hide,
    "Real target disappearance still hides bubble");

var repeatedAction = Machine();
repeatedAction.Advance(Observation(action: true));
Check(!repeatedAction.Advance(Observation(action: true)).StepCompleted,
    "Repeated action never completes without conditions");
Check(repeatedAction.Advance(Observation(conditions: true)).StepCompleted,
    "Repeated action completes once conditions hold");

var delegateObservations = 0;
var delegatePresentations = new List<UnifiedPresentationAction>();
var delegateWaits = 0;
var delegateAdapter = new DelegateUnifiedStepPlatformAdapter(
    (_, _) => Task.FromResult(new UnifiedStepObservation(true, true, true, true,
        ++delegateObservations > 1, delegateObservations > 1)),
    (_, action, _) => { delegatePresentations.Add(action); return Task.CompletedTask; },
    (_, _, _) => throw new Exception("Manual run must not automate"),
    (_, _, _) => { delegateWaits++; return Task.CompletedTask; });
await new UnifiedStepRunner().RunAsync(
    new GuideStep("delegate", 1, null, new BubbleDefinition("Test"),
        new ValidationDefinition("clicked")),
    new UnifiedStepOptions(UnifiedLearnerMode.Manual),
    delegateAdapter, CancellationToken.None);
Check(delegateWaits == 1, "Delegate adapter receives wait notifications");
Check(delegatePresentations.SequenceEqual(new[] {
    UnifiedPresentationAction.Show, UnifiedPresentationAction.Hide
}), "Delegate adapter bridges bubble lifecycle");

var informationStep = new GuideStep("summary", 55, null,
    new BubbleDefinition("Guide completed", BubblePlacement.Center),
    AdvanceMode: StepAdvanceMode.Manual);
var information = new UnifiedStepStateMachine(informationStep);
var prematureDismissal = new UnifiedStepObservation(false, false, false, false,
    false, false, ManualAdvanceRequested: true);
Check(!information.Advance(prematureDismissal).StepCompleted,
    "Centered information cannot dismiss before presentation");
var informationShow = information.Advance(new UnifiedStepObservation(false, false, false, false,
    false, false));
Check(informationShow.PresentationAction == UnifiedPresentationAction.Show,
    "Centered information requests presentation without a target");
information.AcknowledgePresentation(informationShow.PresentationAction);
Check(!information.Advance(new UnifiedStepObservation(false, false, false, false,
    false, false)).StepCompleted, "Centered information waits for manual dismissal");
Check(information.Advance(prematureDismissal).StepCompleted,
    "Centered information completes after manual dismissal");
Check(information.Advance(prematureDismissal).PresentationAction == UnifiedPresentationAction.None,
    "Completed information never requests presentation again");

var manualTarget = new UnifiedStepStateMachine(new GuideStep("manual-target", 1, null,
    new BubbleDefinition("Manual"), AdvanceMode: StepAdvanceMode.Manual));
Check(!manualTarget.Advance(Observation(primary: true, conditions: true)).StepCompleted,
    "Manual target step ignores automatic validation");
Check(manualTarget.Advance(new UnifiedStepObservation(true, true, true, true,
    false, false, ManualAdvanceRequested: true)).StepCompleted,
    "Manual target step advances only on explicit request");

Console.WriteLine("All unified state-machine checks passed.");

sealed class FakeUnifiedAdapter : IUnifiedStepPlatformAdapter
{
    public int AutomationCalls { get; private set; }
    public bool FailOnWait { get; set; }
    public List<UnifiedPresentationAction> PresentationActions { get; } = new();
    private bool _committed;

    public Task<UnifiedStepObservation> ObserveAsync(GuideStep step, CancellationToken token)
        => Task.FromResult(new UnifiedStepObservation(true, true, true, true, _committed, _committed));
    public Task SetPresentationAsync(GuideStep step, UnifiedPresentationAction action, CancellationToken token)
    {
        PresentationActions.Add(action);
        return Task.CompletedTask;
    }
    public Task ApplyAutomationAsync(GuideStep step, string value, CancellationToken token)
    {
        AutomationCalls++;
        return Task.CompletedTask;
    }
    public Task WaitForChangeAsync(GuideStep step, GuideStepReconciliationResult reason, CancellationToken token)
    {
        if (FailOnWait) throw new InvalidOperationException("Simulated platform failure");
        _committed = true;
        return Task.CompletedTask;
    }
}


