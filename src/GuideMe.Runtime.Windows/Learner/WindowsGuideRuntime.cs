using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using GuideMe.Core.Guides;
using GuideMe.Core.Targets;
using GuideMe.Runtime.Windows.Bubbles;
using GuideMe.Runtime.Windows.Targets;
using GuideMe.Runtime.Windows.Validation;

namespace GuideMe.Runtime.Windows.Learner;

public sealed class WindowsGuideRuntime
{
    private readonly WindowsTargetResolver _resolver;
    private readonly WindowsBubblePresenter _bubbles;
    private readonly WindowsValidationEvaluator _validation;
    private readonly TimeSpan _pollInterval;
    private readonly Action<bool>? _bubbleAvailabilityChanged;
    private readonly Dictionary<string, string> _completionConditionDiagnostics = new(StringComparer.Ordinal);
    private bool? _lastBubbleAvailable;

    private void ReportBubbleAvailable(bool available)
    {
        if (_lastBubbleAvailable == available) return;
        _lastBubbleAvailable = available;
        _bubbleAvailabilityChanged?.Invoke(available);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private const byte VirtualKeyTab = 0x09;
    private const uint KeyEventKeyUp = 0x0002;

    public WindowsGuideRuntime(
        WindowsTargetResolver resolver,
        WindowsBubblePresenter bubbles,
        WindowsValidationEvaluator? validation = null,
        TimeSpan? pollInterval = null,
        Action<bool>? bubbleAvailabilityChanged = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _validation = validation ?? new WindowsValidationEvaluator();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
        _bubbleAvailabilityChanged = bubbleAvailabilityChanged;
    }

    public Task RunAsync(
        AutomationElement windowRoot,
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken,
        IReadOnlyList<GuideVariableDefinition>? variables = null, string language = "he", Action<int, int>? onStepStarted = null)
    {
        return new GuideExecutionEngine().RunAsync(guideSteps, CreateStepAdapter(windowRoot), cancellationToken, variables, language, onStepStarted);
    }

    public IGuideStepAdapter CreateStepAdapter(AutomationElement windowRoot)
    {
        ArgumentNullException.ThrowIfNull(windowRoot);
        AutomationElement? preExistingTargetForCurrentStep = null;
        return new DelegateGuideStepAdapter(TargetRuntime.Windows, "Windows",
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
                    preExistingTargetForCurrentStep, plan);
                preExistingTargetForCurrentStep = nextTargetBeforeCurrentAction;
            },
            (step, total, token) => _bubbles.WaitForCenteredStepDismissalAsync(
                step, step.Order, total, token),
            (step, action, token) => Task.FromResult(ResolveVariable(windowRoot, action)));
    }

    private async Task RunStepAsync(
        AutomationElement windowRoot,
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken,
        AutomationElement? preExistingTarget,
        GuideRunPlan plan)
    {
        if (GuideStepExecutionPolicy.Classify(step) == GuideStepPresentationKind.CenteredInformation)
        {
            ReportBubbleAvailable(false);

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

        ReportBubbleAvailable(false);
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
        // Per-session, file-backed diagnostics: stdout is not available for GUI launches.
        // Record state transitions only, not every reconciliation poll.
        var diagnosticPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuideMe", "Logs", $"windows-step-{Environment.ProcessId}.log");
        string? lastDiagnosticState = null;
        void Diagnose(string state)
        {
            if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") != "1")
                return;
            if (string.Equals(lastDiagnosticState, state, StringComparison.Ordinal))
                return;
            lastDiagnosticState = state;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
                File.AppendAllText(diagnosticPath,
                    $"{DateTimeOffset.UtcNow:O} step={step.Order} id={step.Id} {state}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Diagnose($"started validation={step.Validation?.Kind ?? "none"} conditions={step.CompletionConditions?.Count ?? 0}");
        var clickCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationEventHandler? clickHandler = null;
        AutomationElement? subscribedTarget = null;
        AutomationElement? subscribedSelectionTarget = null;

        void DiagnoseTiming(string phase, long elapsedMs)
        {
            if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") != "1")
                return;
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "GuideMe-variable-diagnostic.log"),
                    $"{DateTimeOffset.UtcNow:O} event=windows-step-phase step={step.Order} phase={phase} elapsedMs={elapsedMs}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var resolutionAttempt = 0;

        var initialVisibilityChecked = false;
        var initialInputFocusApplied = false;

        var baselineWatch = Stopwatch.StartNew();
        var completionTargetsBeforeAction = CaptureReplacementBaselines(windowRoot, step);
        baselineWatch.Stop();
        DiagnoseTiming("completion-baselines", baselineWatch.ElapsedMilliseconds);


        // Authoritative target resolution below already checks the exact scoped
        // descendant. Avoid repeating the same expensive provider query before
        // the first resolution; the regular reconciliation loop handles rerenders.

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
                if (sharedEngine.ObserveContext(activeState, IsTargetWindowInteractive(windowRoot))
                    == GuideStepReconciliationResult.WaitingForContext)
                {
                    Diagnose($"window-not-foreground clickObserved={clickCompleted.Task.IsCompleted}");
                    ReportBubbleAvailable(false);
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForContext, _pollInterval, cancellationToken);
                }

                // Completion is evaluated before the source context. A valid learner
                // action may navigate away from that context while persisted
                // post-action conditions become true on the destination screen.
                if (clicked && clickCompleted.Task.IsCompleted)
                {
                    // Once activation was observed, the source control may have been
                    // removed by navigation. Only the destination completion conditions
                    // matter now; never re-resolve the obsolete source or its context.
                    var conditionsSatisfied = AreCompletionConditionsSatisfied(
                        windowRoot, step, completionTargetsBeforeAction);
                    if (sharedEngine.EvaluateCompletion(activeState, true, conditionsSatisfied)
                        == GuideStepReconciliationResult.Completed)
                        return GuideStepReconciliationResult.Completed;

                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                }

                var contextWatch = Stopwatch.StartNew();
                var stepContextActive = IsStepContextActive(windowRoot, step);
                contextWatch.Stop();
                DiagnoseTiming("step-context", contextWatch.ElapsedMilliseconds);
                if (!stepContextActive)
                    Diagnose($"step-context-inactive clickObserved={clickCompleted.Task.IsCompleted}");

                if (sharedEngine.ObserveContext(activeState, stepContextActive)
                    == GuideStepReconciliationResult.WaitingForContext)
                {
                    ReportBubbleAvailable(false);
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForContext, _pollInterval, cancellationToken);
                }

                TargetResolution<AutomationElement> resolution = TargetResolution<AutomationElement>.NotFound();

                resolutionAttempt++;
                try
                {
                    // Re-resolve against the live UIA tree after every grid refresh.
                    // Reusing a cached element could bind to a stale or different row.
                    var lookupWatch = Stopwatch.StartNew();
                    resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), step.Target);
                    lookupWatch.Stop();
                    DiagnoseTiming("target-resolution", lookupWatch.ElapsedMilliseconds);

                }
                catch (ElementNotAvailableException)
                {
                    ReportBubbleAvailable(false);
                    await _bubbles.HideAsync();
                    if (IsDisappearedTargetStepComplete())
                        return GuideStepReconciliationResult.Completed;
                    return await GuideActiveStepState.WaitAsync(
                        sharedEngine.ObserveReadiness(activeState, contextActive: true, targetAvailable: false, targetVisible: false), _pollInterval, cancellationToken);
                }

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    Diagnose($"target={resolution.Status} clickObserved={clickCompleted.Task.IsCompleted}");
                    ReportBubbleAvailable(false);
                    await _bubbles.HideAsync();
                    if (IsDisappearedTargetStepComplete())
                        return GuideStepReconciliationResult.Completed;
                    // Match Web: the shared step state invalidates presentation when
                    // the target disappears, rather than returning a raw wait reason.
                    return await GuideActiveStepState.WaitAsync(
                        sharedEngine.ObserveReadiness(activeState, contextActive: true, targetAvailable: false, targetVisible: false), _pollInterval, cancellationToken);
                }

                var target = resolution.Target;
                if (clicked)
                    Diagnose($"target-resolved clickObserved={clickCompleted.Task.IsCompleted}");
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
                                // Leaving a focused Edit control is an explicit commit
                                // attempt even when its already-valid value is unchanged.
                                // Validation still decides whether the Step may advance.
                                if (Volatile.Read(ref textTargetObservedFocused) == 1)
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
                        && !target.Current.HasKeyboardFocus)
                    {
                        Volatile.Write(ref textTargetCommitted, 1);
                    }
                }


                var sameAsPreExisting = clicked
                    && preExistingTarget is not null
                    && SameElement(preExistingTarget, target);

                // Match the Web learner behavior at Step entry: if the newly
                // resolved target is clipped by the application window or by a
                // scrollable ancestor, bring it into view once before presenting
                // the bubble. Do not repeat this during reconciliation, otherwise
                // GuideMe would fight intentional learner scrolling.
                if (!initialVisibilityChecked)
                {
                    initialVisibilityChecked = true;

                    var needsAdjustment = NeedsInitialViewportAdjustment(windowRoot, target);

                    var scrollSucceeded = needsAdjustment && TryScrollIntoComfortableView(target);

                    if (scrollSucceeded)
                    {
                        if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") == "1")
                            Console.Error.WriteLine(
                            $"[GuideMe Windows guide] Step '{step.Id}' centered initial target in its scroll viewport.");
                        return await GuideActiveStepState.WaitAsync(
                            GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                    }
                }

                var targetVisible = HasVisibleBounds(target);
                var readinessResult = sharedEngine.ObserveReadiness(
                    activeState, contextActive: true, targetAvailable: true, targetVisible: targetVisible);

                if (readinessResult == GuideStepReconciliationResult.WaitingForTarget)
                {
                    // Do not force-scroll during reconciliation. Initial Step entry
                    // already performs the one allowed viewport adjustment. Keep
                    // completion/validation semantics independent of bubble
                    // placement; the presenter decides whether a partially clipped
                    // target is suitable for showing an attached bubble.
                    ReportBubbleAvailable(false);
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

                    clickHandler = (_, _) => { Diagnose("invoke-event"); clickCompleted.TrySetResult(); };

                    Automation.AddAutomationEventHandler(
                        InvokePattern.InvokedEvent,
                        target,
                        TreeScope.Element,
                        clickHandler);

                    subscribedTarget = target;
                    Diagnose("invoke-subscribed");
                }

                if (clicked)
                {
                    // Grid rows commonly activate via SelectionItem rather than Invoke.
                    // Subscribe only to the selected target or its nearest selectable row.
                    AutomationElement? selectionTarget = target;
                    while (selectionTarget is not null &&
                           !selectionTarget.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _))
                        selectionTarget = TreeWalker.ControlViewWalker.GetParent(selectionTarget);
                    if (selectionTarget is not null &&
                        (subscribedSelectionTarget is null ||
                         !SameElement(subscribedSelectionTarget, selectionTarget)))
                    {
                        if (subscribedSelectionTarget is not null && clickHandler is not null)
                        {
                            try { Automation.RemoveAutomationEventHandler(
                                SelectionItemPattern.ElementSelectedEvent, subscribedSelectionTarget, clickHandler); }
                            catch (ElementNotAvailableException) { }
                        }
                        clickHandler ??= (_, _) => { Diagnose("selection-event"); clickCompleted.TrySetResult(); };
                        Automation.AddAutomationEventHandler(
                            SelectionItemPattern.ElementSelectedEvent, selectionTarget,
                            TreeScope.Element, clickHandler);
                        subscribedSelectionTarget = selectionTarget;
                        Diagnose("selection-subscribed");
                    }
                }

                try
                {
                    await _bubbles.ShowAsync(
                        target, step, stepNumber, totalSteps, cancellationToken,
                        null);
                    ReportBubbleAvailable(true);
                }
                catch (ElementNotAvailableException)
                {
                    // The target was removed or virtualized after resolution.
                    // Retry resolution on the next reconciliation cycle.
                    ReportBubbleAvailable(false);
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                }
                catch (InvalidOperationException) when (!HasVisibleBounds(target))
                {
                    // UIA targets can be replaced or lose their bounds between
                    // reconciliation and presentation (for example while WPF
                    // navigates from a grid to a detail view). Treat that race as
                    // transient: hide the stale bubble and resolve again instead
                    // of terminating the learner runtime.
                    ReportBubbleAvailable(false);
                    await _bubbles.HideAsync();
                    return await GuideActiveStepState.WaitAsync(
                        GuideStepReconciliationResult.WaitingForAction, _pollInterval, cancellationToken);
                }


                // Initial input focus belongs to the production learner Runtime,
                // regardless of the presentation flow. Apply it
                // only once; reconciliation must never steal focus back.
                if (!initialInputFocusApplied && IsInputFocusTarget(target))
                {
                    // Mark the one-time focus action before invoking UIA. The runtime can
                    // begin its learner action as soon as the Step-start marker is
                    // observed; setting this flag first prevents a later reconciliation
                    // pass from re-focusing the first editor after Hybrid has already
                    // committed it with TAB.
                    initialInputFocusApplied = true;
                    try { target.SetFocus(); }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }

                    if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") == "1")
                        Console.Error.WriteLine(
                        $"[GuideMe Windows guide] input ready Step {step.Order} '{step.Id}'.");
                }

                if (clicked)
                {
                    if (clickCompleted.Task.IsCompleted)
                        Diagnose($"click-observed conditions={AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction)}");
                    // A clicked Step completes only from observed activation of the
                    // resolved target. Target disappearance by itself is not enough:
                    // asynchronous WPF rerenders can replace a Button without any
                    // learner action and would otherwise create a false completion.
                    if (clickCompleted.Task.IsCompleted
                        && sharedEngine.EvaluateCompletion(activeState, 
                            true,
                            AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction)) == GuideStepReconciliationResult.Completed)
                    {
                        return GuideStepReconciliationResult.Completed;
                    }
                }
                else if (targetDisappeared)
                {
                    // Completion is detected on a later reconciliation pass when
                    // the previously resolved target leaves the current UI tree.

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
                // A manually advanced, target-attached step has no automatic completion
                // signal. Keep its resolved target and bubble visible instead of
                // terminating the session. Explicit manual advancement requires a
                // separate learner navigation action and must not be fabricated here.

                // Once the target is stable and its bubble is visible, reduce expensive
                // full-tree UIA reconciliation. Keep a bounded refresh to detect grid
                // rerenders, target replacement, and viewport changes without caching
                // an element indefinitely. Pending clicks retain the fast poll interval.
                var stablePollInterval = clicked && clickCompleted.Task.IsCompleted
                    ? _pollInterval
                    : TimeSpan.FromMilliseconds(Math.Max(_pollInterval.TotalMilliseconds, 750));
                return await GuideActiveStepState.WaitAsync(
                    GuideStepReconciliationResult.WaitingForAction, stablePollInterval, cancellationToken);
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
            if (subscribedSelectionTarget is not null && clickHandler is not null)
            {
                try { Automation.RemoveAutomationEventHandler(
                    SelectionItemPattern.ElementSelectedEvent, subscribedSelectionTarget, clickHandler); }
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
            void DiagnoseCondition(string state)
            {
                if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") != "1")
                    return;
                var key = step.Id + "|" + condition.Kind + "|" +
                    condition.Target.Locator.Strategy + "|" + condition.Target.Locator.Value;
                if (_completionConditionDiagnostics.TryGetValue(key, out var previous) && previous == state)
                    return;
                _completionConditionDiagnostics[key] = state;
                try
                {
                    var path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GuideMe", "Logs", $"windows-step-{Environment.ProcessId}.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path,
                        $"{DateTimeOffset.UtcNow:O} step={step.Order} id={step.Id} completion-kind={condition.Kind} " +
                        $"locator={condition.Target.Locator.Strategy}:'{condition.Target.Locator.Value}' " +
                        $"anchors={condition.Target.Anchors.Count} result={state}{Environment.NewLine}");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
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
            if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") == "1")
            {
                try
                {
                    var diagnosticPath = Path.Combine(Path.GetTempPath(), "GuideMe-variable-diagnostic.log");
                    var expected = condition.ExpectedValue ?? "<null>";
                    var fallbackEligible = resolution.Status == TargetResolutionStatus.NotFound
                        && (kind is "value-contains" or "value-equals")
                        && !string.IsNullOrEmpty(condition.ExpectedValue);
                    File.AppendAllText(diagnosticPath,
                        $"{DateTimeOffset.UtcNow:O} event=windows-completion-entry step={step.Order} " +
                        $"process={Environment.ProcessId} expected='{expected}' kind='{kind}' " +
                        $"initialStatus={resolution.Status} fallbackEligible={fallbackEligible} " +
                        $"targetType='{condition.Target.ControlType}' " +
                        $"locator='{condition.Target.Locator.Strategy}:{condition.Target.Locator.Value}'" +
                        Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (resolution.Status == TargetResolutionStatus.NotFound
                && kind is "value-contains" or "value-equals"
                && !string.IsNullOrEmpty(condition.ExpectedValue))
            {
                // A captured label can change after navigation. Match its live
                // value using the captured control type and structural anchors.
                // Preserve ambiguity and require the value policy to pass.
                try
                {
                    resolution = _resolver.ResolveByObservedValue(
                        GetActiveResolutionRoot(windowRoot), condition.Target,
                        condition.ExpectedValue, kind == "value-contains");
                }
                catch (ElementNotAvailableException)
                {
                    resolution = TargetResolution<AutomationElement>.NotFound();
                }
            }
            if (GuideCompletionPolicy.IsSupportedObservationKind(kind))
            {
                var resolved = resolution.Status == TargetResolutionStatus.Resolved && resolution.Target is not null;
                var ambiguous = resolution.Status == TargetResolutionStatus.Ambiguous;
                var enabled = false;
                string? observedValue = null;
                if (resolved && kind == "target-enabled")
                    enabled = resolution.Target!.Current.IsEnabled;
                if (resolved && kind is "value-equals" or "value-contains")
                {
                    // Static text and grid labels expose their content through UIA Name,
                    // not ValuePattern. Prefer ValuePattern for editable controls.
                    var observedTarget = resolution.Target!;
                    observedValue = observedTarget.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern)
                        ? ((ValuePattern)valuePattern).Current.Value
                        : observedTarget.Current.Name;
                }
                var satisfied = GuideCompletionPolicy.IsSatisfied(condition, resolved, ambiguous, enabled, observedValue);
                DiagnoseCondition($"status={resolution.Status} satisfied={satisfied}" +
                    (resolved ? $" controlType={resolution.Target!.Current.ControlType.ProgrammaticName} name='{resolution.Target.Current.Name}'" : ""));
                if (!satisfied)
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

    private string? ResolveVariable(AutomationElement windowRoot, GuideVariableAction action)
    {
        var descriptor = action.SourceTarget
            ?? throw new InvalidOperationException($"Variable action '{action.Id}' has no target.");
        if (descriptor.Runtime != TargetRuntime.Windows)
            throw new InvalidOperationException("Windows runtime can read only Windows variables.");
        var resolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), descriptor);
        if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
            return null;
        string? raw;
        try
        {
            raw = action.Property?.Trim().ToLowerInvariant() switch
            {
                "name" => resolution.Target.Current.Name,
                "automation-id" => resolution.Target.Current.AutomationId,
                _ => throw new NotSupportedException(
                    $"Unsupported Windows variable property '{action.Property}'.")
            };
        }
        catch (ElementNotAvailableException) { return null; }
        if (string.IsNullOrEmpty(action.Pattern)) return raw;
        var match = Regex.Match(raw ?? string.Empty, action.Pattern, RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
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
                // as uncomfortable for a new GuideMe presentation so the bubble gets
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
