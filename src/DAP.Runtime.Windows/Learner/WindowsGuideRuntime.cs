using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Windows.Bubbles;
using DAP.Runtime.Windows.Targets;
using DAP.Runtime.Windows.Validation;

namespace DAP.Runtime.Windows.Learner;

public sealed class WindowsGuideRuntime
{
    private static readonly Regex RuntimeValueToken = new(@"\{\{step:(?<step>[^}:]+):capture\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly WindowsTargetResolver _resolver;
    private readonly WindowsBubblePresenter _bubbles;
    private readonly WindowsValidationEvaluator _validation;
    private readonly TimeSpan _pollInterval;

    public WindowsGuideRuntime(
        WindowsTargetResolver resolver,
        WindowsBubblePresenter bubbles,
        WindowsValidationEvaluator? validation = null,
        TimeSpan? pollInterval = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
        _validation = validation ?? new WindowsValidationEvaluator();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task RunAsync(
        AutomationElement windowRoot,
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken,
        int? startStepOrder = null)
    {
        var ordered = guideSteps.OrderBy(step => step.Order).ToArray();
        var startIndex = 0;

        if (startStepOrder is not null)
        {
            startIndex = Array.FindIndex(ordered, step => step.Order == startStepOrder.Value);
            if (startIndex < 0)
                throw new InvalidOperationException($"Guide does not contain Step order {startStepOrder.Value}.");
        }

        AutomationElement? preExistingTargetForCurrentStep = null;
        var capturedValues = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = startIndex; index < ordered.Length; index++)
        {
            var persistedStep = ordered[index];
            var step = MaterializeRuntimeValues(persistedStep, capturedValues);
            if (step.Target?.Runtime != TargetRuntime.Windows)
                throw new InvalidOperationException(
                    $"Guide Step '{step.Id}' is not a Windows Step and cannot run in WindowsGuideRuntime.");

            AutomationElement? nextTargetBeforeCurrentAction = null;
            if (index + 1 < ordered.Length)
            {
                var nextPersistedStep = ordered[index + 1];
                var nextStep = TryMaterializeRuntimeValues(nextPersistedStep, capturedValues);
                if (nextStep?.Target?.Runtime == TargetRuntime.Windows)
                {
                    try
                    {
                        var nextResolution = _resolver.Resolve(GetActiveResolutionRoot(windowRoot), nextStep.Target);
                        if (nextResolution.Status == TargetResolutionStatus.Resolved)
                            nextTargetBeforeCurrentAction = nextResolution.Target;
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                }
            }

            Console.Error.WriteLine($"[DAP Windows guide] starting Step {index + 1}/{ordered.Length} '{step.Id}'.");
            await RunStepAsync(
                windowRoot,
                step,
                index + 1,
                ordered.Length,
                cancellationToken,
                preExistingTargetForCurrentStep,
                capturedValues);
            Console.Error.WriteLine($"[DAP Windows guide] completed Step {index + 1}/{ordered.Length} '{step.Id}'.");

            preExistingTargetForCurrentStep = nextTargetBeforeCurrentAction;
        }

        await _bubbles.HideAsync();
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
        var clicked = string.Equals(step.Validation?.Kind, "clicked", StringComparison.OrdinalIgnoreCase);
        var targetDisappeared = string.Equals(step.Validation?.Kind, "target-disappeared", StringComparison.OrdinalIgnoreCase);
        var targetWasResolved = false;
        var clickCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationEventHandler? clickHandler = null;
        AutomationElement? subscribedTarget = null;
        string? lastTargetDisappearedDiagnostic = null;
        var stepStopwatch = Stopwatch.StartNew();
        var resolutionAttempt = 0;
        var targetFirstResolvedLogged = false;
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
            while (!cancellationToken.IsCancellationRequested)
            {
                // Keep learner overlays bound to the target application. When the
                // target is minimized or the user switches to another application,
                // hide both bubble and highlight. The next reconciliation pass
                // restores them from fresh UIA bounds when the target becomes active.
                if (!IsTargetWindowInteractive(windowRoot))
                {
                    await _bubbles.HideAsync();
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
                }

                // Completion is evaluated before the source context. A valid learner
                // action may navigate away from that context while persisted
                // post-action conditions become true on the destination screen.
                if (clicked
                    && clickCompleted.Task.IsCompleted
                    && AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction))
                {
                    FinalizeCapture(windowRoot, step, capturedValues);
                    return;
                }

                if (!IsStepContextActive(windowRoot, step))
                {
                    await _bubbles.HideAsync();
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
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
                    if (targetDisappeared && targetWasResolved
                        && AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction))
                        return;
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
                }

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    if (step.Id == "testcrm-windows-back-to-cases")
                        Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' resolution status={resolution.Status}; targetNull={resolution.Target is null}.");
                    await _bubbles.HideAsync();
                    if (targetDisappeared && targetWasResolved
                        && AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction))
                        return;
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
                }

                var target = resolution.Target;
                targetWasResolved = true;

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
                    if (capture is not null
                        && (!capturedValues.TryGetValue(step.Id, out var previousCapture)
                            || !string.Equals(previousCapture, capture, StringComparison.Ordinal)))
                    {
                        capturedValues[step.Id] = capture;
                        Console.Error.WriteLine(
                            $"[DAP Windows guide] updated runtime capture for Step '{step.Id}' to '{capture}'.");
                    }
                }

                if (!HasVisibleBounds(target))
                {
                    var scrollStartedAt = stepStopwatch.ElapsedMilliseconds;
                    TryScrollIntoView(target);
                    Console.Error.WriteLine(
                        $"[DAP Windows step timing] Step '{step.Id}' ScrollIntoView finished at " +
                        $"+{stepStopwatch.ElapsedMilliseconds} ms " +
                        $"(duration={stepStopwatch.ElapsedMilliseconds - scrollStartedAt} ms).");
                    if (!HasVisibleBounds(target))
                    {
                        if (step.Id == "testcrm-windows-back-to-cases")
                            Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' target has no visible bounds.");
                        await _bubbles.HideAsync();
                        await Task.Delay(_pollInterval, cancellationToken);
                        continue;
                    }
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
                await _bubbles.ShowAsync(target, step, stepNumber, totalSteps, cancellationToken);
                Console.Error.WriteLine(
                    $"[DAP Windows step timing] Step '{step.Id}' bubble shown at " +
                    $"+{stepStopwatch.ElapsedMilliseconds} ms " +
                    $"(ShowAsync duration={stepStopwatch.ElapsedMilliseconds - bubbleStartedAt} ms).");

                if (step.Id == "testcrm-windows-back-to-cases")
                    Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' bubble shown.");

                if (clicked)
                {
                    // A clicked Step completes only from observed activation of the
                    // resolved target. Target disappearance by itself is not enough:
                    // asynchronous WPF rerenders can replace a Button without any
                    // learner action and would otherwise create a false completion.
                    if (clickCompleted.Task.IsCompleted
                        && AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction))
                    {
                        FinalizeCapture(windowRoot, step, capturedValues);
                        return;
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
                else if (step.AdvanceMode == StepAdvanceMode.AutomaticOnValidation
                         && step.Validation is not null
                         && _validation.IsSatisfied(target, step.Validation)
                         && AreCompletionConditionsSatisfied(windowRoot, step, completionTargetsBeforeAction))
                {
                    return;
                }
                else if (step.AdvanceMode == StepAdvanceMode.Manual)
                {
                    throw new NotSupportedException("Manual Windows Steps are not implemented yet.");
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }
        }
        finally
        {
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
            if (kind == "target-exists")
            {
                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                    return false;
                continue;
            }

            if (kind == "target-not-exists")
            {
                if (resolution.Status == TargetResolutionStatus.Resolved)
                    return false;
                if (resolution.Status == TargetResolutionStatus.Ambiguous)
                    return false;
                continue;
            }

            if (kind == "target-enabled")
            {
                if (resolution.Status != TargetResolutionStatus.Resolved
                    || resolution.Target is null
                    || !resolution.Target.Current.IsEnabled)
                    return false;
                continue;
            }

            if (kind == "value-equals")
            {
                if (resolution.Status != TargetResolutionStatus.Resolved
                    || resolution.Target is null
                    || condition.ExpectedValue is null
                    || !_validation.IsSatisfied(
                        resolution.Target,
                        new ValidationDefinition("value-equals", condition.ExpectedValue)))
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

        capturedValues[step.Id] = value;
        Console.Error.WriteLine($"[DAP Windows guide] finalized runtime capture for Step '{step.Id}' as '{value}'.");
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

    private static GuideStep MaterializeRuntimeValues(GuideStep step, IReadOnlyDictionary<string, string> values)
    {
        if (step.Target is null)
            return step;

        string Replace(string value) => RuntimeValueToken.Replace(value, match =>
        {
            var source = match.Groups["step"].Value;
            if (!values.TryGetValue(source, out var captured))
                throw new InvalidOperationException($"Guide Step '{step.Id}' references uncaptured runtime value from Step '{source}'.");
            return captured;
        });

        return step with
        {
            Target = step.Target with
            {
                Locator = step.Target.Locator with { Value = Replace(step.Target.Locator.Value) },
                Anchors = step.Target.Anchors
                    .Select(anchor => anchor with { Locator = anchor.Locator with { Value = Replace(anchor.Locator.Value) } })
                    .ToArray()
            }
        };
    }

    private static GuideStep? TryMaterializeRuntimeValues(GuideStep step, IReadOnlyDictionary<string, string> values)
    {
        try { return MaterializeRuntimeValues(step, values); }
        catch (InvalidOperationException) { return null; }
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

    private static void TryScrollIntoView(AutomationElement target)
    {
        try
        {
            if (target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var pattern))
                ((ScrollItemPattern)pattern).ScrollIntoView();
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
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
