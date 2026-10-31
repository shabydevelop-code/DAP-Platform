using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using GuideMe.Core.Targets;

namespace GuideMe.Runtime.Windows.Targets;

public sealed class WindowsTargetResolver
{
    private static readonly object DiagnosticGate = new();
    private static readonly HashSet<string> DiagnosedLocators = new(StringComparer.Ordinal);

    private static void DiagnoseExactLookup(AutomationElement root, Locator locator, int matchCount)
    {
        if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") != "1")
            return;
        var key = locator.Strategy + ":" + locator.Value + ":" + matchCount;
        lock (DiagnosticGate)
        {
            if (!DiagnosedLocators.Add(key)) return;
        }

        try
        {
            var nearby = new List<string>();
            if (matchCount > 1)
            {
                // Inspect the actual matching elements without changing resolution behavior.
                foreach (var element in Find(root, locator).Take(4))
                {
                    try
                    {
                        var rect = element.Current.BoundingRectangle;
                        var parent = TreeWalker.RawViewWalker.GetParent(element);
                        var parentName = parent?.Current.Name ?? "";
                        var parentType = parent?.Current.ControlType.ProgrammaticName ?? "";
                        nearby.Add($"name='{element.Current.Name}' type='{element.Current.ControlType.ProgrammaticName}' automationId='{element.Current.AutomationId}' offscreen={element.Current.IsOffscreen} bounds='{rect}' parentName='{parentName}' parentType='{parentType}'");
                    }
                    catch (ElementNotAvailableException) { }
                    catch (System.Runtime.InteropServices.COMException) { }
                }
            }
            var rootName = root.Current.Name;
            var message = $"{DateTimeOffset.UtcNow:O} event=windows-target-lookup strategy='{locator.Strategy}' value='{locator.Value}' matches={matchCount} root='{rootName}' nearby=[{string.Join("; ", nearby)}]{Environment.NewLine}";
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "GuideMe-variable-diagnostic.log"), message);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            // Diagnostics must never interrupt the learner.
        }
    }

    public TargetResolution<AutomationElement> Resolve(AutomationElement root, TargetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Runtime != TargetRuntime.Windows)
            throw new InvalidOperationException("WindowsTargetResolver can resolve only Windows targets.");

        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var cpuStarted = process.TotalProcessorTime;

        // Never assume a repeated name or AutomationId identifies a unique grid cell.
        // An unanchored locator must resolve to exactly one element.
        if (descriptor.Anchors.Count == 0 && IsUniqueExactLocator(descriptor.Locator))
        {
            var matches = FindForCapturedType(root, descriptor.Locator, descriptor.ControlType).Take(2).ToArray();
            stopwatch.Stop();
            if (Environment.GetEnvironmentVariable("GUIDEME_VARIABLE_DIAGNOSTICS") == "1")
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "GuideMe-variable-diagnostic.log"),
                        $"{DateTimeOffset.UtcNow:O} event=windows-target-timing strategy='{descriptor.Locator.Strategy}' value='{descriptor.Locator.Value}' controlType='{descriptor.ControlType}' matches={matches.Length} elapsedMs={stopwatch.ElapsedMilliseconds}{Environment.NewLine}");
                }
                catch (IOException) { }
            }
            DiagnoseExactLookup(root, descriptor.Locator, matches.Length);
            return matches.Length switch
            {
                0 => TargetResolution<AutomationElement>.NotFound(),
                1 => TargetResolution<AutomationElement>.Resolved(matches[0]),
                _ => TargetResolution<AutomationElement>.Ambiguous(matches.Length)
            };
        }

        // Self anchors describe properties of the target itself. Push native exact
        // predicates into UIA instead of enumerating every primary match first.
        // Keep all non-self anchors for the existing validation pass.
        var nativeSelfAnchors = descriptor.Anchors
            .Where(anchor => anchor.Relation == AnchorRelation.Self
                && TryCreateNativeCondition(anchor.Locator, out _))
            .ToArray();
        if (nativeSelfAnchors.Length > 0
            && TryCreateNativeCondition(descriptor.Locator, out var primaryCondition))
        {
            var conditions = new List<Condition> { primaryCondition };
            foreach (var anchor in nativeSelfAnchors)
            {
                TryCreateNativeCondition(anchor.Locator, out var condition);
                conditions.Add(condition);
            }
            var combined = new AndCondition(conditions.ToArray());
            var matched = FilterCapturedType(root.FindAll(TreeScope.Descendants,
                TryGetControlType(descriptor.ControlType, out var nativeType)
                    ? new AndCondition(combined, new PropertyCondition(AutomationElement.ControlTypeProperty, nativeType))
                    : combined).Cast<AutomationElement>(), descriptor.ControlType)
                .Where(candidate => descriptor.Anchors
                    .Where(anchor => !nativeSelfAnchors.Contains(anchor))
                    .All(anchor => MatchesAnchor(candidate, anchor)))
                .ToList();
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds >= 100)
                Console.Error.WriteLine(
                    $"[GuideMe Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                    $"fast-path=native-self-anchors, final={matched.Count}, elapsed={stopwatch.ElapsedMilliseconds} ms.");
            return matched.Count switch
            {
                0 => TargetResolution<AutomationElement>.NotFound(),
                1 => TargetResolution<AutomationElement>.Resolved(matched[0]),
                _ => TargetResolution<AutomationElement>.Ambiguous(matched.Count)
            };
        }

        var primaryFindStopwatch = Stopwatch.StartNew();
        var primaryFindCpuStarted = process.TotalProcessorTime;
        var candidates = FindForCapturedType(root, descriptor.Locator, descriptor.ControlType).ToList();
        primaryFindStopwatch.Stop();
        var primaryFindCpu = process.TotalProcessorTime - primaryFindCpuStarted;
        var primaryCandidateCount = candidates.Count;
        var anchorDiagnostics = new List<string>(descriptor.Anchors.Count);

        foreach (var anchor in descriptor.Anchors)
        {
            var before = candidates.Count;
            var anchorStopwatch = Stopwatch.StartNew();
            var anchorCpuStarted = process.TotalProcessorTime;
            candidates = candidates.Where(candidate => MatchesAnchor(candidate, anchor)).ToList();
            anchorStopwatch.Stop();
            var anchorCpu = process.TotalProcessorTime - anchorCpuStarted;
            anchorDiagnostics.Add(
                $"{anchor.Relation}:{anchor.Locator.Strategy}='{anchor.Locator.Value}' {before}->{candidates.Count} " +
                $"wall={anchorStopwatch.ElapsedMilliseconds}ms cpu={anchorCpu.TotalMilliseconds:F0}ms");
        }

        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds >= 100 || primaryCandidateCount >= 100)
        {
            var anchors = anchorDiagnostics.Count == 0
                ? "none"
                : string.Join(", ", anchorDiagnostics);
            Console.Error.WriteLine(
                $"[GuideMe Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                $"primary={primaryCandidateCount}, primaryWall={primaryFindStopwatch.ElapsedMilliseconds} ms, " +
                $"primaryCpu={primaryFindCpu.TotalMilliseconds:F0} ms, anchors=[{anchors}], final={candidates.Count}, " +
                $"elapsed={stopwatch.ElapsedMilliseconds} ms, cpu={(process.TotalProcessorTime - cpuStarted).TotalMilliseconds:F0} ms.");
        }

        return candidates.Count switch
        {
            0 => TargetResolution<AutomationElement>.NotFound(),
            1 => TargetResolution<AutomationElement>.Resolved(candidates[0]),
            _ => TargetResolution<AutomationElement>.Ambiguous(candidates.Count)
        };
    }

    private static IEnumerable<AutomationElement> FindForCapturedType(
        AutomationElement root, Locator locator, string? capturedType)
    {
        if (TryGetControlType(capturedType, out var type)
            && TryCreateNativeCondition(locator, out var locatorCondition))
        {
            var condition = new AndCondition(locatorCondition,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            return root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>();
        }
        return FilterCapturedType(Find(root, locator), capturedType);
    }

    private static bool TryGetControlType(string? programmaticName, out ControlType type)
    {
        type = ControlType.Custom;
        if (string.IsNullOrWhiteSpace(programmaticName))
            return false;
        var property = typeof(ControlType).GetProperties(System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static)
            .FirstOrDefault(p => p.PropertyType == typeof(ControlType)
                && string.Equals("ControlType." + p.Name, programmaticName, StringComparison.Ordinal));
        if (property?.GetValue(null) is not ControlType found)
            return false;
        type = found;
        return true;
    }

    private static IEnumerable<AutomationElement> FilterCapturedType(
        IEnumerable<AutomationElement> candidates, string? controlType)
    {
        if (string.IsNullOrWhiteSpace(controlType))
            return candidates;
        return candidates.Where(element =>
        {
            try { return string.Equals(element.Current.ControlType.ProgrammaticName, controlType, StringComparison.Ordinal); }
            catch (ElementNotAvailableException) { return false; }
        });
    }

    private static AutomationElement? FindRawAncestor(AutomationElement candidate, Locator locator)
    {
        var walker = TreeWalker.RawViewWalker;
        for (AutomationElement? current = candidate; current is not null; current = walker.GetParent(current))
            if (MatchesLocator(current, locator))
                return current;

        return null;
    }

    private static bool IsUniqueExactLocator(Locator locator)
    {
        var strategy = locator.Strategy.Trim();
        return strategy.Equals("automation-id", StringComparison.OrdinalIgnoreCase)
               || strategy.Equals("name", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNativeExactLocator(Locator locator) =>
        TryCreateNativeCondition(locator, out _);

    private static bool IsExactRegexLocator(Locator locator) =>
        locator.Strategy.Trim().Equals("name-regex", StringComparison.OrdinalIgnoreCase)
        && TryGetExactRegexValue(locator.Value, out _);

    private static AutomationElement? FindFirst(AutomationElement root, Locator locator)
    {
        if (!TryCreateNativeCondition(locator, out var condition))
            return Find(root, locator).FirstOrDefault();

        return root.FindFirst(TreeScope.Descendants, condition);
    }

    private static IEnumerable<AutomationElement> Find(AutomationElement root, Locator locator)
    {
        if (locator.Strategy.Trim().Equals("name-regex", StringComparison.OrdinalIgnoreCase))
        {
            if (TryCreateNativeCondition(locator, out var exactCondition))
                return root.FindAll(TreeScope.Descendants, exactCondition).Cast<AutomationElement>();
            var regex = new Regex(locator.Value, RegexOptions.CultureInvariant);
            return root.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(element =>
                {
                    try
                    {
                        return regex.IsMatch(element.Current.Name ?? string.Empty);
                    }
                    catch (ElementNotAvailableException)
                    {
                        // Dynamic UIA trees can invalidate an element between
                        // FindAll and reading Current.Name. Skip only that element.
                        return false;
                    }
                })
                .ToArray();
        }

        var condition = CreateCondition(locator);
        return root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>();
    }

    private static Condition CreateCondition(Locator locator)
    {
        if (TryCreateNativeCondition(locator, out var condition))
            return condition;

        throw new NotSupportedException(
            $"Unsupported Windows locator strategy '{locator.Strategy}'.");
    }

    private static bool TryCreateNativeCondition(Locator locator, out Condition condition)
    {
        switch (locator.Strategy.Trim().ToLowerInvariant())
        {
            case "automation-id":
                condition = new PropertyCondition(AutomationElement.AutomationIdProperty, locator.Value);
                return true;
            case "name":
                condition = new PropertyCondition(AutomationElement.NameProperty, locator.Value);
                return true;
            case "control-type":
                condition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ParseControlType(locator.Value));
                return true;
            case "name-regex" when TryGetExactRegexValue(locator.Value, out var exactName):
                condition = new PropertyCondition(AutomationElement.NameProperty, exactName);
                return true;
            default:
                condition = Condition.FalseCondition;
                return false;
        }
    }

    private static bool TryGetExactRegexValue(string pattern, out string value)
    {
        value = string.Empty;
        if (pattern.Length < 2 || pattern[0] != '^' || pattern[^1] != '$')
            return false;

        var body = pattern[1..^1];
        if (body.Length == 0)
            return true;

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

        try
        {
            value = Regex.Unescape(body);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ControlType ParseControlType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "button" => ControlType.Button,
            "edit" or "textbox" => ControlType.Edit,
            "combobox" => ControlType.ComboBox,
            "dataitem" => ControlType.DataItem,
            "window" => ControlType.Window,
            _ => throw new NotSupportedException($"Unsupported Windows control type '{value}'.")
        };

    private static bool MatchesAnchors(AutomationElement candidate, IReadOnlyList<Anchor> anchors) =>
        anchors.All(anchor => MatchesAnchor(candidate, anchor));

    private static bool MatchesAnchor(AutomationElement candidate, Anchor anchor) =>
        anchor.Relation switch
        {
            AnchorRelation.Self =>
                MatchesLocator(candidate, anchor.Locator),
            AnchorRelation.Ancestor or AnchorRelation.Context =>
                HasInParentChain(candidate, anchor.Locator),
            AnchorRelation.Descendant =>
                HasDescendant(candidate, anchor.Locator),
            AnchorRelation.Sibling =>
                HasSibling(candidate, anchor.Locator),
            AnchorRelation.Nearby =>
                HasSibling(candidate, anchor.Locator) || HasInParentChain(candidate, anchor.Locator),
            _ => false
        };

    private static bool HasDescendant(AutomationElement candidate, Locator locator)
    {
        if (TryCreateNativeCondition(locator, out var condition))
            return candidate.FindFirst(TreeScope.Descendants, condition) is not null;

        return candidate.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Any(element => MatchesLocator(element, locator));
    }

    private static bool HasInParentChain(AutomationElement candidate, Locator locator)
    {
        var walker = TreeWalker.ControlViewWalker;
        for (var current = walker.GetParent(candidate); current is not null; current = walker.GetParent(current))
            if (MatchesLocator(current, locator))
                return true;
        return false;
    }

    private static bool HasSibling(AutomationElement candidate, Locator locator)
    {
        var walker = TreeWalker.ControlViewWalker;
        var parent = walker.GetParent(candidate);
        if (parent is null)
            return false;

        foreach (AutomationElement sibling in parent.FindAll(TreeScope.Children, Condition.TrueCondition))
            if (!Automation.Compare(candidate, sibling) && MatchesLocator(sibling, locator))
                return true;

        return false;
    }

    private static bool MatchesLocator(AutomationElement element, Locator locator)
    {
        if (locator.Strategy.Trim().Equals("name-regex", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(element.Current.Name ?? string.Empty, locator.Value, RegexOptions.CultureInvariant);

        return MatchesCondition(element, CreateCondition(locator));
    }

    private static bool MatchesCondition(AutomationElement element, Condition condition)
    {
        if (condition is not PropertyCondition property)
            return false;

        var actual = element.GetCurrentPropertyValue(property.Property, true);
        // UIA returns a ControlType object for ControlTypeProperty, while
        // PropertyCondition.Value exposes its numeric control type ID.
        // Normalize both representations before comparing.
        if (property.Property == AutomationElement.ControlTypeProperty)
        {
            var actualId = actual is ControlType actualType ? actualType.Id
                : actual is int actualNumber ? actualNumber : (int?)null;
            var expectedId = property.Value is ControlType expectedType ? expectedType.Id
                : property.Value is int expectedNumber ? expectedNumber : (int?)null;
            return actualId.HasValue && expectedId.HasValue && actualId.Value == expectedId.Value;
        }
        return Equals(actual, property.Value);
    }
}
