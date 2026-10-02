using System.Diagnostics;
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
                        var nextResolution = _resolver.Resolve(windowRoot, nextStep.Target);
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
        var clickedDisappearanceFallbackArmed = false;
        var clickCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationEventHandler? clickHandler = null;
        AutomationElement? subscribedTarget = null;
        string? lastTargetDisappearedDiagnostic = null;
        var stepStopwatch = Stopwatch.StartNew();
        var resolutionAttempt = 0;
        var targetFirstResolvedLogged = false;
        var bubbleShownForStep = false;

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
                TargetResolution<AutomationElement> resolution;
                var resolutionStopwatch = Stopwatch.StartNew();
                resolutionAttempt++;
                try
                {
                    resolution = _resolver.Resolve(windowRoot, step.Target!);
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
                    if (!bubbleShownForStep)
                        await _bubbles.HideAsync();
                    if (targetDisappeared && targetWasResolved)
                        return;
                    if (clicked && clickedDisappearanceFallbackArmed)
                        return;
                    await Task.Delay(_pollInterval, cancellationToken);
                    continue;
                }

                if (resolution.Status != TargetResolutionStatus.Resolved || resolution.Target is null)
                {
                    if (step.Id == "testcrm-windows-back-to-cases")
                        Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' resolution status={resolution.Status}; targetNull={resolution.Target is null}.");
                    if (!bubbleShownForStep)
                        await _bubbles.HideAsync();
                    if (targetDisappeared && targetWasResolved)
                        return;
                    if (clicked && clickedDisappearanceFallbackArmed)
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
                if (clicked && !sameAsPreExisting)
                    clickedDisappearanceFallbackArmed = true;

                if (step.Id == "testcrm-windows-back-to-cases")
                {
                    Console.Error.WriteLine(
                        $"[DAP Windows guide diagnostic] Step '{step.Id}' resolved; " +
                        $"sameAsPreExisting={sameAsPreExisting}; " +
                        $"fallbackArmed={clickedDisappearanceFallbackArmed}; " +
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
                        if (!bubbleShownForStep)
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
                bubbleShownForStep = true;
                Console.Error.WriteLine(
                    $"[DAP Windows step timing] Step '{step.Id}' bubble shown at " +
                    $"+{stepStopwatch.ElapsedMilliseconds} ms " +
                    $"(ShowAsync duration={stepStopwatch.ElapsedMilliseconds - bubbleStartedAt} ms).");

                if (step.Id == "testcrm-windows-back-to-cases")
                    Console.Error.WriteLine($"[DAP Windows guide diagnostic] Step '{step.Id}' bubble shown.");

                if (clicked)
                {
                    if (clickCompleted.Task.IsCompleted)
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
                         && _validation.IsSatisfied(target, step.Validation))
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
        var resolution = _resolver.Resolve(windowRoot, descriptor);
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
        if (pattern.Length < 2 || pattern[0] != '^' || pattern[^1] != '
        try
        {
            return Automation.Compare(left, right);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

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
)
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
