using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Windows.Bubbles;
using DAP.Runtime.Windows.Targets;
using DAP.Runtime.Windows.Validation;

namespace DAP.Runtime.Windows.Learner;

public sealed class WindowsGuideRuntime
{
    private readonly WindowsTargetResolver _resolver;
    private readonly WindowsBubblePresenter _bubbles;
    private readonly WindowsValidationEvaluator _validation;
    private readonly TimeSpan _pollInterval;
    private readonly string? _automaticStepLabel;
    private readonly bool _hybrid;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private const byte VirtualKeyTab = 0x09;
    private const uint KeyEventKeyUp = 0x0002;

    public WindowsGuideRuntime(
        WindowsTargetResolver resolver,
        WindowsBubblePresenter bubbles,
        WindowsValidationEvaluator? validation = null,
        TimeSpan? pollInterval = null,
        string? automaticStepLabel = null,
        bool hybrid = false)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _validation = validation ?? new WindowsValidationEvaluator();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
        _automaticStepLabel = automaticStepLabel;
        _hybrid = hybrid;
    }

    public async Task RunAsync(
        AutomationElement windowRoot,
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken,
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null)
    {
        AutomationElement? preExistingTargetForCurrentStep = null;

        await new GuideExecutionEngine().RunAsync(
            guideSteps,
            new DelegateGuideStepAdapter(TargetRuntime.Windows, "Windows",
            async (step, index, total, plan, token) =>
            {
                var ordered = plan.Steps;
                AutomationElement? nextTargetBeforeCurrentAction = null;
                var nextEnabledIndex = plan.NextEnabledIndex(index);
                if (nextEnabledIndex >= 0)
                {
                    var nextStep = plan.TryMaterialize(ordered[nextEnabledIndex]);
                    if (nextStep?.Target?.Runtime == TargetRuntime.Windows)
                    {
                        try
                        {
                            var resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), nextStep.Target);
                            if (resolution.Status == TargetResolutionStatus.Resolved)
                                nextTargetBeforeCurrentAction = resolution.Target;
                        }
                        catch (ElementNotAvailableException)
                        {
                        }
                    }
                }

                await RunStepAsync(
                    windowRoot, step, step.Order, total, token,
                    preExistingTargetForCurrentStep, plan.Captures);
                preExistingTargetForCurrentStep = nextTargetBeforeCurrentAction;
            },
            (step, total, token) => _bubbles.WaitForCenteredStepDismissalAsync(
                step, step.Order, total, token),
            (step, plan, token) => Task.CompletedTask),
            cancellationToken,
            startStepOrder,
            initialCapturedValues);

    }

    private async Task RunStepAsync(
        AutomationElement windowRoot,
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken,
        AutomationElement? preExistingTarget,
        IDictionary<string, string> capturedValues)
    {
        if (GuideStepExecutionPolicy.Classify(step) == GuideStepPresentationKind.CenteredInformation)
        {

            await _bubbles.WaitForCenteredStepDismissalAsync(
                step,
                stepNumber,
                totalSteps,
                cancellationToken);
            return;
        }

        if (step.Target is null)
            throw new InvalidOperationException(
                $"Target-attached Guide Step '{step.Id}' must define a target.");

        var activeState = new GuideActiveStepState(step);
        var sharedEngine = new UnifiedGuideStepEngine();
        var clicked = GuideStepExecutionPolicy.IsClickValidationStep(step);
        var targetDisappeared = GuideStepExecutionPolicy.IsTargetDisappearanceStep(step);
        var targetWasResolved = false;
        string? initialTextValue = null;
        var textTargetObservedFocused = 0;
        var textTargetChanged = 0;
        var textTargetCommitted = 0;
        AutomationElement? subscribedTextTarget = null;
        AutomationPropertyChangedEventHandler? textEditPropertyChangedHandler = null;
        var clickCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationEventHandler? clickHandler = null;
        AutomationElement? subscribedTarget = null;
        string? lastTargetDisappearedDiagnostic = null;
        var stepStopwatch = Stopwatch.StartNew();
        var resolutionAttempt = 0;
        var targetFirstResolvedLogged = false;
        var bubbleFirstShownLogged = false;
        var initialVisibilityChecked = false;
        var initialInputFocusApplied = false;
        var completionTargetsBeforeAction = CaptureReplacementBaselines(windowRoot, step);

        Console.Error.WriteLine(
            $"[DAP Windows step timing] Step {stepNumber}/{totalSteps} '{step.Id}' entered at +0 ms.");

        if (ShouldWaitForUiStability(step.Target!))
        {
            var settleStartedAt = stepStopwatch.ElapsedMilliseconds;
            await WaitForTargetScopeStabilityAsync(windowRoot, step.Target!, cancellationToken);
            Console.Error.WriteLine(
                $"[DAP Windows step timing] Step '{step.Id}' UIA scope settled at " +
                $"+{stepStopwatch.ElapsedMilliseconds} ms " +
                $"(wait={stepStopwatch.ElapsedMilliseconds - settleStartedAt} ms).");
        }

        try
        {
            bool IsDisappearedTargetStepComplete()
                => targetDisappeared && targetWasResolved
                    && sharedEngine.EvaluateCompletion(activeState, 
                        true, AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction))
                        == GuideStepReconciliationResult.Completed;

            async Task<GuideStepReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
            {
                // Keep learner overlays bound to the target application. When the
                // target is minimized or the user switches to another application,
                // hide both bubble and highlight. The next reconciliation pass
                // restores them from fresh UIA bounds when the target becomes active.
                if (activeState.ObserveContext(IsTargetWindowInteractive(windowRoot))
                    == GuideStepReconciliationResult.WaitingForContext)
                {
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForContext, _pollInterval, cancellationToken);
                }

                // Completion is evaluated before the source context. A valid learner
                // action may navigate away from that context while persisted
                // post-action conditions become true on the destination screen.
                if (clicked && clickCompleted.Task.IsCompleted && sharedEngine.EvaluateCompletion(activeState, 
                            true,
                            AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction)) == GuideStepReconciliationResult.Completed)
                {
                    FinalizeCapture(windowRoot, step, capturedValues);
                    return GuideStepReconciliationResult.Completed;
                }

                if (activeState.ObserveContext(IsStepContextActive(windowRoot, step))
                    == GuideStepReconciliationResult.WaitingForContext)
                {
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForContext, _pollInterval, cancellationToken);
                }

                TargetResolution<AutomationElement> resolution;
                var resolutionStopwatch = Stopwatch.StartNew();
                resolutionAttempt++;
                try
                {
                    resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), step.Target!);
                    resolutionStopwatch.Stop();
                    if (resolutionStopwatch.ElapsedMilliseconds >= 100)
                    {
                        Console.Error.WriteLine(
                            $"[DAP Windows step timing] Step '{step.Id}' resolution attempt {resolutionAttempt} " +
                            $"status={resolution.Status}, duration={resolutionStopwatch.ElapsedMilliseconds} ms, " +
                            $"stepElapsed={stepStopwatch.ElapsedMilliseconds} ms.");
                    }
                }
                catch (ElementNotAvailableException)
                {
                    if (step.Id == "testcrm-windows-back-to-cases")
                        Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' resolver threw ElementNotAvailableException.");
                    await _bubbles.HideAsync();
                    if (IsDisappearedTargetStepComplete())
                        return GuideStepReconciliationResult.Completed;
                    return await GuideActiveStepState.WaitAsync(
                        sharedEngine.ObserveReadiness(activeState, contextActive: true, targetAvailable: false, targetVisible: false), _pollInterval, cancellationToken);
                }

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    if (step.Id == "testcrm-windows-back-to-cases")
                        Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' resolution status={resolution.Status}; targetNull={resolution.Target is null}.");
                    await _bubbles.HideAsync();
                    if (IsDisappearedTargetStepComplete())
                        return GuideStepReconciliationResult.Completed;
                    // Match Web: the shared step state invalidates presentation when
                    // the target disappears, rather than returning a raw wait reason.
                    return await GuideActiveStepState.WaitAsync(
                        sharedEngine.ObserveReadiness(activeState, contextActive: true, targetAvailable: false, targetVisible: false), _pollInterval, cancellationToken);
                }

                var target = resolution.Target;
                activeState.ObserveTarget(true);
                targetWasResolved = true;

                var isTextEditTarget = target.Current.ControlType == ControlType.Edit
                    && GuideStepExecutionPolicy.IsAutomaticValidationStep(step)
                    && !clicked
                    && !targetDisappeared;

                if (isTextEditTarget && target.TryGetCurrentPattern(ValuePattern.Pattern, out var textValuePattern))
                {
                    var currentTextValue = ((ValuePattern)textValuePattern).Current.Value;

                    if (subscribedTextTarget is null || !SameElement(subscribedTextTarget, target))
                    {
                        if (subscribedTextTarget is not null && textEditPropertyChangedHandler is not null)
                        {
                            try
                            {
                                Automation.RemoveAutomationPropertyChangedEventHandler(
                                    subscribedTextTarget,
                                    textEditPropertyChangedHandler);
                            }
                            catch (ElementNotAvailableException)
                            {
                            }
                        }

                        subscribedTextTarget = target;
                        initialTextValue = currentTextValue;

                        textEditPropertyChangedHandler = (_, args) =>
                        {
                            if (args.Property == ValuePattern.ValueProperty)
                            {
                                if (!Equals(args.OldValue, args.NewValue))
                                {
                                    Volatile.Write(ref textTargetChanged, 1);

                                    // A fast edit can begin and lose focus entirely
                                    // between reconciliation polls. Capture the
                                    // focus state at the value-change signal itself
                                    // so the later blur/poll can still form a real
                                    // edit -> blur commit.
                                    try
                                    {
                                        if (subscribedTextTarget?.Current.HasKeyboardFocus == true)
                                            Volatile.Write(ref textTargetObservedFocused, 1);
                                    }
                                    catch (ElementNotAvailableException)
                                    {
                                    }
                                }

                                return;
                            }

                            if (args.Property != AutomationElement.HasKeyboardFocusProperty)
                                return;

                            if (args.NewValue is bool hasKeyboardFocus && hasKeyboardFocus)
                            {
                                Volatile.Write(ref textTargetObservedFocused, 1);
                                return;
                            }

                            if (args.NewValue is bool lostKeyboardFocus && !lostKeyboardFocus)
                            {
                                if (Volatile.Read(ref textTargetObservedFocused) == 1
                                    && Volatile.Read(ref textTargetChanged) == 1)
                                    Volatile.Write(ref textTargetCommitted, 1);
                            }
                        };

                        Automation.AddAutomationPropertyChangedEventHandler(
                            target,
                            TreeScope.Element,
                            textEditPropertyChangedHandler,
                            ValuePattern.ValueProperty,
                            AutomationElement.HasKeyboardFocusProperty);
                    }

                    initialTextValue ??= currentTextValue;

                    // Keep polling as a fallback for providers that do not emit
                    // every UIA property notification. The target-scoped
                    // HasKeyboardFocusProperty transition is the primary blur
                    // signal, so fast focus loss cannot be missed between polls.
                    if (target.Current.HasKeyboardFocus)
                        Volatile.Write(ref textTargetObservedFocused, 1);

                    if (!string.Equals(currentTextValue, initialTextValue, StringComparison.Ordinal))
                        Volatile.Write(ref textTargetChanged, 1);

                    if (Volatile.Read(ref textTargetObservedFocused) == 1
                        && Volatile.Read(ref textTargetChanged) == 1
                        && !target.Current.HasKeyboardFocus)
                    {
                        Volatile.Write(ref textTargetCommitted, 1);
                    }
                }

                if (!targetFirstResolvedLogged)
                {
                    targetFirstResolvedLogged = true;
                    Console.Error.WriteLine(
                        $"[DAP Windows step timing] Step '{step.Id}' target first resolved at " +
                        $"+{stepStopwatch.ElapsedMilliseconds} ms after {resolutionAttempt} attempt(s).");
                }

                var sameAsPreExisting = clicked
                    && preExistingTarget is not null
                    && SameElement(preExistingTarget, target);
                if (step.Id == "testcrm-windows-back-to-cases")
                {
                    Console.Error.WriteLine(
                        $"[DAP Windows guide diagnostic] Step '{step.Id}' resolved; " +
                        $"sameAsPreExisting={sameAsPreExisting}; " +
                        $"target={DescribeTarget(target)}");
                }

                if (step.Capture is not null)
                {
                    var capture = ResolveCapture(windowRoot, step.Capture);
                    if (GuideRunPlan.RecordCapture(capturedValues, step.Id, capture))
                    {
                        Console.Error.WriteLine(
                            $"[DAP Windows guide] updated runtime capture for Step '{step.Id}' to '{capture}'.");
                    }
                }

                // Match the Web learner behavior at Step entry: if the newly
                // resolved target is clipped by the application window or by a
                // scrollable ancestor, bring it into view once before presenting
                // the bubble. Do not repeat this during reconciliation, otherwise
                // DAP would fight intentional learner scrolling.
                if (!initialVisibilityChecked)
                {
                    initialVisibilityChecked = true;
                    if (NeedsInitialViewportAdjustment(windowRoot, target)
                        && TryScrollIntoComfortableView(target))
                    {
                        Console.Error.WriteLine(
                            $"[DAP Windows guide] Step '{step.Id}' centered initial target in its scroll viewport.");
                        return await GuideActiveStepState.WaitAsync(
                            GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                    }
                }

                if (sharedEngine.ObserveReadiness(activeState, contextActive: true, targetAvailable: true, targetVisible: HasVisibleBounds(target))
                    == GuideStepReconciliationResult.WaitingForTarget)
                {
                    // Do not force-scroll during reconciliation. Initial Step entry
                    // already performs the one allowed viewport adjustment. Keep
                    // completion/validation semantics independent of bubble
                    // placement; the presenter decides whether a partially clipped
                    // target is suitable for showing an attached bubble.
                    if (step.Id == "testcrm-windows-back-to-cases")
                        Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' target has no visible bounds.");
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                }

                if (clicked && (subscribedTarget is null || !SameElement(subscribedTarget, target)))
                {
                    if (subscribedTarget is not null && clickHandler is not null)
                    {
                        try { Automation.RemoveAutomationEventHandler(InvokePattern.InvokedEvent, subscribedTarget, clickHandler); }
                        catch (ElementNotAvailableException) { }
                    }

                    clickHandler = (_, _) => clickCompleted.TrySetResult();
                    Automation.AddAutomationEventHandler(
                        InvokePattern.InvokedEvent,
                        target,
                        TreeScope.Element,
                        clickHandler);
                    subscribedTarget = target;
                    if (step.Id == "testcrm-windows-back-to-cases")
                        Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' subscribed to Invoke.");
                }

                if (step.Id == "testcrm-windows-back-to-cases")
                    Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' showing bubble.");

                var bubbleStartedAt = stepStopwatch.ElapsedMilliseconds;
                try
                {
                    await _bubbles.ShowAsync(
                        target, step, stepNumber, totalSteps, cancellationToken,
                        !string.IsNullOrEmpty(step.AutomationValue) ? _automaticStepLabel : null);
                }
                catch (InvalidOperationException) when (!HasVisibleBounds(target))
                {
                    // UIA targets can be replaced or lose their bounds between
                    // reconciliation and presentation (for example while WPF
                    // navigates from a grid to a detail view). Treat that race as
                    // transient: hide the stale bubble and resolve again instead
                    // of terminating the learner runtime.
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                }
                if (!bubbleFirstShownLogged)
                {
                    Console.Error.WriteLine(
                        $"[DAP Windows step timing] Step '{step.Id}' first bubble shown at " +
                        $"+{stepStopwatch.ElapsedMilliseconds} ms " +
                        $"(ShowAsync duration={stepStopwatch.ElapsedMilliseconds - bubbleStartedAt} ms).");
                    bubbleFirstShownLogged = true;
                }

                // Initial input focus belongs to the production learner Runtime,
                // so Manual and Hybrid start each input Step identically. Apply it
                // only once; reconciliation must never steal focus back.
                if (!initialInputFocusApplied && IsInputFocusTarget(target))
                {
                    // Mark the one-time focus action before invoking UIA. Hybrid can
                    // begin its learner action as soon as the Step-start marker is
                    // observed; setting this flag first prevents a later reconciliation
                    // pass from re-focusing the first editor after Hybrid has already
                    // committed it with TAB.
                    initialInputFocusApplied = true;
                    try { target.SetFocus(); }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }

                    Console.Error.WriteLine(
                        $"[DAP Windows guide] input ready Step {step.Order} '{step.Id}'.");
                }

                if (activeState.ShouldApplyHybridValue(_hybrid, targetResolved: true))
                {
                    GuideStepExecutionPolicy.RequireHybridValueStep(step, "Windows");
                    if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                        throw new NotSupportedException($"Hybrid Step '{step.Id}' declares an automation value but the resolved target does not support UIA ValuePattern.");
                    var valuePattern = (ValuePattern)pattern;
                    if (valuePattern.Current.IsReadOnly)
                        throw new InvalidOperationException($"Hybrid Step '{step.Id}' targets a read-only control.");
                    if (!IsTargetWindowInteractive(windowRoot))
                        throw new InvalidOperationException($"Hybrid Step '{step.Id}' cannot act while the target application is inactive.");

                    // The runtime owns both the value change and its commit. UIA SetValue
                    // alone does not commit WPF Edit controls; their validation is
                    // intentionally armed on an edit -> blur transition.
                    if (isTextEditTarget && !target.Current.HasKeyboardFocus)
                        target.SetFocus();
                    if (isTextEditTarget && !target.Current.HasKeyboardFocus)
                        throw new InvalidOperationException($"Hybrid Step '{step.Id}' could not focus its input.");
                    if (isTextEditTarget)
                        Volatile.Write(ref textTargetObservedFocused, 1);

                    valuePattern.SetValue(step.AutomationValue);
                    if (!string.Equals(valuePattern.Current.Value, step.AutomationValue, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Hybrid Step '{step.Id}' did not retain the declared value.");

                    if (isTextEditTarget)
                    {
                        Volatile.Write(ref textTargetChanged, 1);
                        // Yield to UIA property-change notifications before committing.
                        await Task.Delay(100, cancellationToken);
                        if (!target.Current.HasKeyboardFocus || !IsTargetWindowInteractive(windowRoot))
                            throw new InvalidOperationException($"Hybrid Step '{step.Id}' lost focus before commit.");
                        keybd_event(VirtualKeyTab, 0, 0, UIntPtr.Zero);
                        keybd_event(VirtualKeyTab, 0, KeyEventKeyUp, UIntPtr.Zero);
                        // Observe the actual blur, not merely the injected key.
                        var blurStarted = Stopwatch.StartNew();
                        while (target.Current.HasKeyboardFocus && blurStarted.Elapsed < TimeSpan.FromSeconds(5))
                            await Task.Delay(50, cancellationToken);
                        if (target.Current.HasKeyboardFocus)
                            throw new InvalidOperationException($"Hybrid Step '{step.Id}' TAB did not commit the input.");
                        Volatile.Write(ref textTargetCommitted, 1);
                    }
                    activeState.MarkHybridValueApplied();
                    Console.Error.WriteLine($"[DAP Windows Hybrid] committed persisted value for Step {step.Order} '{step.Id}'.");
                }

                if (step.Id == "testcrm-windows-back-to-cases")
                    Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' bubble shown.");

                if (clicked)
                {
                    // A clicked Step completes only from observed activation of the
                    // resolved target. Target disappearance by itself is not enough:
                    // asynchronous WPF rerenders can replace a Button without any
                    // learner action and would otherwise create a false completion.
                    if (clickCompleted.Task.IsCompleted
                        && sharedEngine.EvaluateCompletion(activeState, 
                            true,
                            AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction)) == GuideStepReconciliationResult.Completed)
                    {
                        FinalizeCapture(windowRoot, step, capturedValues);
                        return GuideStepReconciliationResult.Completed;
                    }
                }
                else if (targetDisappeared)
                {
                    // Completion is detected on a later reconciliation pass when
                    // the previously resolved target leaves the current UI tree.
                    var diagnostic = DescribeTarget(target);
                    if (!string.Equals(diagnostic, lastTargetDisappearedDiagnostic, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine(
                            $"[DAP Windows guide] Step '{step.Id}' target-disappeared still resolves: {diagnostic}");
                        lastTargetDisappearedDiagnostic = diagnostic;
                    }
                }
                else if (GuideStepExecutionPolicy.CanEvaluatePrimaryValidation(
                    step, isTextEditTarget, Volatile.Read(ref textTargetCommitted) == 1))
                {
                    var primaryValidationSatisfied = _validation.IsSatisfied(target, step.Validation!);
                    if (sharedEngine.EvaluateCompletion(activeState, 
                            primaryValidationSatisfied,
                            AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction),
                        isTextEditTarget, Volatile.Read(ref textTargetCommitted) == 1) == GuideStepReconciliationResult.Completed)
                    {
                        return GuideStepReconciliationResult.Completed;
                    }

                    if (isTextEditTarget
                        && GuideStepExecutionPolicy.ShouldConsumeInvalidCommit(
                            step, Volatile.Read(ref textTargetCommitted) == 1, primaryValidationSatisfied)
                        && target.TryGetCurrentPattern(ValuePattern.Pattern, out var committedValuePattern))
                    {
                        // A text commit is one blur attempt. If that committed
                        // value is invalid, consume the attempt and establish a
                        // fresh baseline. Further typing must not advance until
                        // the learner leaves the field again.
                        initialTextValue = ((ValuePattern)committedValuePattern).Current.Value;
                        Volatile.Write(ref textTargetObservedFocused, 0);
                        Volatile.Write(ref textTargetChanged, 0);
                        Volatile.Write(ref textTargetCommitted, 0);
                    }
                }
                else if (step.AdvanceMode == StepAdvanceMode.Manual)
                {
                    throw new NotSupportedException("Manual Windows Steps are not implemented yet.");
                }

                return await GuideActiveStepState.WaitAsync(
                    GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
            }

            await sharedEngine.RunAsync(activeState, (_, token) => ReconcileAsync(token), cancellationToken);
        }
        finally
        {
            if (subscribedTextTarget is not null && textEditPropertyChangedHandler is not null)
            {
                try
                {
                    Automation.RemoveAutomationPropertyChangedEventHandler(
                        subscribedTextTarget,
                        textEditPropertyChangedHandler);
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            if (subscribedTarget is not null && clickHandler is not null)
            {
                try { Automation.RemoveAutomationEventHandler(InvokePattern.InvokedEvent, subscribedTarget, clickHandler); }
                catch (ElementNotAvailableException) { }
            }
            await _bubbles.HideAsync();
        }
    }

    private bool AreCompletionConditionsSatisfied(
        AutomationElement windowRoot,
        GuideStep step,
        IReadOnlyDictionary<StepCompletionCondition, AutomationElement?> replacementBaselines)
    {
        if (step.CompletionConditions is null || step.CompletionConditions.Count == 0)
            return true;

        foreach (var condition in step.CompletionConditions)
        {
            if (condition.Target.Runtime != TargetRuntime.Windows)
                throw new InvalidOperationException(
                    $"Windows Guide Step '{step.Id}' contains a non-Windows completion target.");

            TargetResolution<AutomationElement> resolution;
            try
            {
                resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), condition.Target);
            }
            catch (ElementNotAvailableException)
            {
                resolution = TargetResolution<AutomationElement>.NotFound();
            }

            var kind = condition.Kind.Trim().ToLowerInvariant();
            if (GuideCompletionPolicy.IsSupportedObservationKind(kind))
            {
                var resolved = resolution.Status == TargetResolutionStatus.Resolved && resolution.Target is not null;
                var ambiguous = resolution.Status == TargetResolutionStatus.Ambiguous;
                var enabled = false;
                string? observedValue = null;
                if (resolved && kind == "target-enabled")
                    enabled = resolution.Target!.Current.IsEnabled;
                if (resolved && kind == "value-equals" && resolution.Target!.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
                    observedValue = ((ValuePattern)valuePattern).Current.Value;
                if (!GuideCompletionPolicy.IsSatisfied(condition, resolved, ambiguous, enabled, observedValue))
                    return false;
                continue;
            }

            if (kind == "target-replaced")
            {
                if (!replacementBaselines.TryGetValue(condition, out var before)
                    || before is null
                    || resolution.Status != TargetResolutionStatus.Resolved
                    || resolution.Target is null
                    || SameElement(before, resolution.Target))
                    return false;
                continue;
            }

            throw new NotSupportedException(
                $"Unsupported Windows completion condition kind '{condition.Kind}'.");
        }

        return true;
    }

    private IReadOnlyDictionary<StepCompletionCondition, AutomationElement?> CaptureReplacementBaselines(
        AutomationElement windowRoot,
        GuideStep step)
    {
        var baselines = new Dictionary<StepCompletionCondition, AutomationElement?>();
        if (step.CompletionConditions is null)
            return baselines;

        foreach (var condition in step.CompletionConditions)
        {
            if (!string.Equals(condition.Kind, "target-replaced", StringComparison.OrdinalIgnoreCase))
                continue;

            if (condition.Target.Runtime != TargetRuntime.Windows)
                throw new InvalidOperationException(
                    $"Windows Guide Step '{step.Id}' contains a non-Windows completion target.");

            try
            {
                var resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), condition.Target);
                baselines[condition] = resolution.Status == TargetResolutionStatus.Resolved
                    ? resolution.Target
                    : null;
            }
            catch (ElementNotAvailableException)
            {
                baselines[condition] = null;
            }
        }

        return baselines;
    }

    private static bool IsStepContextActive(AutomationElement windowRoot, GuideStep step)
    {
        if (step.Context is null)
            return true;

        try
        {
            var kind = step.Context.Kind.Trim().ToLowerInvariant();
            Condition condition = kind switch
            {
                "automation-id-exists" => new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    step.Context.Value),
                "name-exists" => new PropertyCondition(
                    AutomationElement.NameProperty,
                    step.Context.Value),
                _ => throw new NotSupportedException(
                    $"Unsupported Windows Step context kind '{step.Context.Kind}'.")
            };

            return GetActiveResolutionRoot(windowRoot)
                .FindFirst(TreeScope.Descendants, condition) is not null;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private void FinalizeCapture(AutomationElement windowRoot, GuideStep step, IDictionary<string, string> capturedValues)
    {
        if (step.Capture is null)
            return;

        var value = ResolveCapture(windowRoot, step.Capture);
        if (value is null)
            return;

        GuideRunPlan.RecordCapture(capturedValues, step.Id, value);
        Console.Error.WriteLine($"[DAP Windows guide] finalized runtime capture for Step '{step.Id}' as '{value}'.");
    }

    public string? CaptureStepValue(AutomationElement windowRoot, GuideStep step)
    {
        if (step.Capture is null)
            return null;

        return ResolveCapture(windowRoot, step.Capture);
    }

    private string? ResolveCapture(AutomationElement windowRoot, StepCaptureDefinition capture)
    {
        if (capture.Runtime != TargetRuntime.Windows)
            throw new InvalidOperationException("WindowsGuideRuntime can capture only Windows runtime values.");

        var descriptor = TargetDescriptor.Create(TargetRuntime.Windows, capture.Locator);
        var resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), descriptor);
        if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
            return null;

        var raw = capture.Property.Trim().ToLowerInvariant() switch
        {
            "name" => resolution.Target.Current.Name,
            "automation-id" => resolution.Target.Current.AutomationId,
            _ => throw new NotSupportedException($"Unsupported Windows capture property '{capture.Property}'.")
        };

        if (string.IsNullOrEmpty(capture.Pattern))
            return raw;

        var match = Regex.Match(raw ?? string.Empty, capture.Pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private async Task WaitForTargetScopeStabilityAsync(
        AutomationElement windowRoot,
        TargetDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var scopeAnchor = descriptor.Anchors.FirstOrDefault(anchor =>
            anchor.Relation is AnchorRelation.Ancestor or AnchorRelation.Context
            && IsSimpleExactLocator(anchor.Locator));
        if (scopeAnchor is null)
            return;

        var scopeWait = Stopwatch.StartNew();
        AutomationElement? scope = null;
        while (scope is null && scopeWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                scope = FindFirstExact(windowRoot, scopeAnchor.Locator);
            }
            catch (ElementNotAvailableException)
            {
            }

            if (scope is null)
                await Task.Delay(_pollInterval, cancellationToken);
        }

        if (scope is null)
            return;

        var observation = Stopwatch.StartNew();
        long lastSignal = Stopwatch.GetTimestamp();
        var signalCount = 0;

        void MarkSignal()
        {
            Interlocked.Exchange(ref lastSignal, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref signalCount);
        }

        StructureChangedEventHandler structureHandler = (_, _) => MarkSignal();
        AutomationEventHandler asyncContentHandler = (_, _) => MarkSignal();

        try
        {
            Automation.AddStructureChangedEventHandler(
                scope,
                TreeScope.Subtree,
                structureHandler);
            Automation.AddAutomationEventHandler(
                AutomationElement.AsyncContentLoadedEvent,
                scope,
                TreeScope.Subtree,
                asyncContentHandler);

            var quietPeriod = TimeSpan.FromMilliseconds(450);
            var maxObservation = TimeSpan.FromSeconds(5);

            while (observation.Elapsed < maxObservation)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var last = Interlocked.Read(ref lastSignal);
                var quietFor = Stopwatch.GetElapsedTime(last);
                if (quietFor >= quietPeriod)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }

            if (signalCount > 0 || observation.ElapsedMilliseconds >= 100)
            {
                Console.Error.WriteLine(
                    $"[DAP Windows UIA settle] scope={scopeAnchor.Locator.Strategy}='{scopeAnchor.Locator.Value}', " +
                    $"signals={signalCount}, elapsed={observation.ElapsedMilliseconds} ms.");
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            try
            {
                Automation.RemoveStructureChangedEventHandler(scope, structureHandler);
            }
            catch (ElementNotAvailableException)
            {
            }

            try
            {
                Automation.RemoveAutomationEventHandler(
                    AutomationElement.AsyncContentLoadedEvent,
                    scope,
                    asyncContentHandler);
            }
            catch (ElementNotAvailableException)
            {
            }
        }
    }

    private static bool ShouldWaitForUiStability(TargetDescriptor descriptor)
    {
        var hasScope = descriptor.Anchors.Any(anchor =>
            anchor.Relation is AnchorRelation.Ancestor or AnchorRelation.Context
            && IsSimpleExactLocator(anchor.Locator));
        var hasExactDynamicDescendant = descriptor.Anchors.Any(anchor =>
            anchor.Relation == AnchorRelation.Descendant
            && IsExactNameRegex(anchor.Locator));

        return hasScope && hasExactDynamicDescendant;
    }

    private static bool IsSimpleExactLocator(Locator locator)
    {
        var strategy = locator.Strategy.Trim();
        return strategy.Equals("automation-id", StringComparison.OrdinalIgnoreCase)
               || strategy.Equals("name", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactNameRegex(Locator locator)
    {
        if (!locator.Strategy.Trim().Equals("name-regex", StringComparison.OrdinalIgnoreCase))
            return false;

        var pattern = locator.Value;
        if (pattern.Length < 2 || pattern[0] != '^' || pattern[^1] != '$')
            return false;

        var body = pattern[1..^1];
        for (var index = 0; index < body.Length; index++)
        {
            if (body[index] == '\\')
            {
                if (++index >= body.Length)
                    return false;

                continue;
            }

            if (".+*?()[]{}|^$".Contains(body[index]))
                return false;
        }

        return true;
    }

    private static AutomationElement? FindFirstExact(AutomationElement root, Locator locator)
    {
        var strategy = locator.Strategy.Trim();
        Condition condition;
        if (strategy.Equals("automation-id", StringComparison.OrdinalIgnoreCase))
            condition = new PropertyCondition(AutomationElement.AutomationIdProperty, locator.Value);
        else if (strategy.Equals("name", StringComparison.OrdinalIgnoreCase))
            condition = new PropertyCondition(AutomationElement.NameProperty, locator.Value);
        else
            return null;

        return root.FindFirst(TreeScope.Descendants, condition);
    }

    private static bool SameElement(AutomationElement left, AutomationElement right)
    {
        try
        {
            return Automation.Compare(left, right);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsTargetWindowInteractive(AutomationElement windowRoot)
    {
        try
        {
            var rootHandle = new IntPtr(windowRoot.Current.NativeWindowHandle);
            if (rootHandle == IntPtr.Zero)
                return true;

            if (IsIconic(rootHandle))
                return false;

            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return true;

            if (foreground == rootHandle || IsChild(rootHandle, foreground))
                return true;

            // Native/WPF modal dialogs are commonly top-level owned windows rather
            // than HWND children. Follow the owner chain so expected CRM modals
            // remain interactive without treating unrelated applications as active.
            for (var current = foreground;
                 current != IntPtr.Zero;
                 current = GetWindow(current, GwOwner))
            {
                if (current == rootHandle)
                    return true;
            }

            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static AutomationElement GetActiveResolutionRoot(AutomationElement windowRoot)
    {
        try
        {
            var handle = new IntPtr(windowRoot.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
                return windowRoot;

            var popup = GetWindow(handle, GwEnabledPopup);
            if (popup == IntPtr.Zero || popup == handle)
                return windowRoot;

            return AutomationElement.FromHandle(popup) ?? windowRoot;
        }
        catch (ElementNotAvailableException)
        {
            return windowRoot;
        }
    }

    private const uint GwOwner = 4;
    private const uint GwEnabledPopup = 6;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    private static bool TryScrollIntoView(AutomationElement target)
    {
        try
        {
            if (!target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var pattern))
                return false;

            ((ScrollItemPattern)pattern).ScrollIntoView();
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryScrollIntoComfortableView(AutomationElement target)
    {
        var scrolled = TryScrollIntoView(target);

        try
        {
            var walker = TreeWalker.ControlViewWalker;
            for (var ancestor = walker.GetParent(target);
                 ancestor is not null;
                 ancestor = walker.GetParent(ancestor))
            {
                if (!ancestor.TryGetCurrentPattern(ScrollPattern.Pattern, out var rawPattern))
                    continue;

                var scroll = (ScrollPattern)rawPattern;
                if (!scroll.Current.VerticallyScrollable)
                    continue;

                var viewport = ancestor.Current.BoundingRectangle;
                var targetRect = target.Current.BoundingRectangle;
                if (viewport.IsEmpty || targetRect.IsEmpty || viewport.Height <= 0)
                    return scrolled;

                var viewSize = scroll.Current.VerticalViewSize;
                var currentPercent = scroll.Current.VerticalScrollPercent;
                if (viewSize <= 0 || viewSize >= 100 || currentPercent < 0)
                    return scrolled;

                var targetCenter = targetRect.Top + targetRect.Height / 2d;
                var viewportCenter = viewport.Top + viewport.Height / 2d;
                var deltaPixels = targetCenter - viewportCenter;

                // UIA exposes viewport size as a percentage of the full content.
                // Convert the physical offset from viewport center into the
                // corresponding scroll-range percentage and center the target.
                var percentDelta =
                    deltaPixels / viewport.Height
                    * (100d * viewSize / (100d - viewSize));
                var desiredPercent = Math.Clamp(currentPercent + percentDelta, 0d, 100d);

                if (Math.Abs(desiredPercent - currentPercent) < 0.25d)
                    return scrolled;

                scroll.SetScrollPercent(ScrollPattern.NoScroll, desiredPercent);
                return true;
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return scrolled;
    }

    private static bool IsFullyVisibleWithinViewport(
        AutomationElement windowRoot,
        AutomationElement target)
    {
        try
        {
            var targetRect = target.Current.BoundingRectangle;
            if (target.Current.IsOffscreen
                || targetRect.IsEmpty
                || targetRect.Width <= 0
                || targetRect.Height <= 0)
                return false;

            var windowRect = windowRoot.Current.BoundingRectangle;
            if (!windowRect.IsEmpty && !ContainsRect(windowRect, targetRect))
                return false;

            var walker = TreeWalker.ControlViewWalker;
            for (var ancestor = walker.GetParent(target);
                 ancestor is not null && !SameElement(ancestor, windowRoot);
                 ancestor = walker.GetParent(ancestor))
            {
                if (!ancestor.TryGetCurrentPattern(ScrollPattern.Pattern, out var rawPattern))
                    continue;

                var scroll = (ScrollPattern)rawPattern;
                if (!scroll.Current.VerticallyScrollable)
                    continue;

                var viewport = ancestor.Current.BoundingRectangle;
                if (!viewport.IsEmpty && !ContainsRect(viewport, targetRect))
                    return false;
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool NeedsInitialViewportAdjustment(
        AutomationElement windowRoot,
        AutomationElement target)
    {
        try
        {
            var targetRect = target.Current.BoundingRectangle;
            if (target.Current.IsOffscreen
                || targetRect.IsEmpty
                || targetRect.Width <= 0
                || targetRect.Height <= 0)
                return true;

            var windowRect = windowRoot.Current.BoundingRectangle;
            if (!windowRect.IsEmpty && !ContainsRect(windowRect, targetRect))
                return true;

            var walker = TreeWalker.ControlViewWalker;
            for (var ancestor = walker.GetParent(target);
                 ancestor is not null && !SameElement(ancestor, windowRoot);
                 ancestor = walker.GetParent(ancestor))
            {
                if (!ancestor.TryGetCurrentPattern(ScrollPattern.Pattern, out var rawPattern))
                    continue;

                var scroll = (ScrollPattern)rawPattern;
                if (!scroll.Current.VerticallyScrollable)
                    continue;

                var viewport = ancestor.Current.BoundingRectangle;
                if (viewport.IsEmpty)
                    continue;

                if (!ContainsRect(viewport, targetRect))
                    return true;

                // UIA ScrollIntoView only guarantees visibility and can leave a
                // target pinned to an edge. Treat the outer 20% of the viewport
                // as uncomfortable for a new DAP presentation so the bubble gets
                // useful space around its target, matching the Web experience.
                var center = targetRect.Top + targetRect.Height / 2d;
                var comfortableTop = viewport.Top + viewport.Height * 0.20d;
                var comfortableBottom = viewport.Bottom - viewport.Height * 0.20d;
                if (center < comfortableTop || center > comfortableBottom)
                    return true;
            }

            return false;
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
    }

    private static bool ContainsRect(System.Windows.Rect outer, System.Windows.Rect inner)
    {
        const double tolerance = 1d;
        return inner.Left >= outer.Left - tolerance
               && inner.Top >= outer.Top - tolerance
               && inner.Right <= outer.Right + tolerance
               && inner.Bottom <= outer.Bottom + tolerance;
    }

    private static bool HasVisibleBounds(AutomationElement target)
    {
        try
        {
            var rect = target.Current.BoundingRectangle;
            return !target.Current.IsOffscreen
                   && !rect.IsEmpty
                   && rect.Width > 0
                   && rect.Height > 0;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsInputFocusTarget(AutomationElement target)
    {
        try
        {
            var controlType = target.Current.ControlType;
            return target.Current.IsKeyboardFocusable
                   && target.Current.IsEnabled
                   && (controlType == ControlType.Edit
                       || controlType == ControlType.ComboBox
                       || controlType == ControlType.List);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static string DescribeTarget(AutomationElement target)
    {
        try
        {
            var parts = new List<string>
            {
                $"AutomationId='{target.Current.AutomationId}'",
                $"Name='{target.Current.Name}'",
                $"ControlType='{target.Current.ControlType?.ProgrammaticName ?? "<null>"}'",
                $"IsOffscreen={target.Current.IsOffscreen}",
                $"IsEnabled={target.Current.IsEnabled}"
            };

            var ancestors = new List<string>();
            var walker = TreeWalker.ControlViewWalker;
            for (var current = walker.GetParent(target);
                 current is not null && ancestors.Count < 6;
                 current = walker.GetParent(current))
            {
                ancestors.Add(
                    $"{current.Current.ControlType?.ProgrammaticName ?? "<null>"}" +
                    $"(AutomationId='{current.Current.AutomationId}',Name='{current.Current.Name}',IsOffscreen={current.Current.IsOffscreen})");
            }

            parts.Add($"Ancestors=[{string.Join(" <- ", ancestors)}]");
            return string.Join("; ", parts);
        }
        catch (ElementNotAvailableException)
        {
            return "<target became unavailable while collecting diagnostics>";
        }
    }
}
