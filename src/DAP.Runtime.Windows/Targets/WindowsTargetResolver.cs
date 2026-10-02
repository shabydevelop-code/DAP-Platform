using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using DAP.Core.Targets;

namespace DAP.Runtime.Windows.Targets;

public sealed class WindowsTargetResolver
{
    public TargetResolution<AutomationElement> Resolve(AutomationElement root, TargetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Runtime != TargetRuntime.Windows)
            throw new InvalidOperationException("WindowsTargetResolver can resolve only Windows targets.");

        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var cpuStarted = process.TotalProcessorTime;

        // An unanchored exact locator is the Guide's declaration that the locator itself is
        // sufficient to identify the target. Use FindFirst so UIA can stop walking large
        // descendant trees (for example, a screen containing a large DataGrid).
        if (descriptor.Anchors.Count == 0 && IsExactLocator(descriptor.Locator))
        {
            var findStopwatch = Stopwatch.StartNew();
            var findCpuStarted = process.TotalProcessorTime;
            var candidate = FindFirst(root, descriptor.Locator);
            findStopwatch.Stop();
            var findCpu = process.TotalProcessorTime - findCpuStarted;
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds >= 100)
            {
                Console.Error.WriteLine(
                    $"[DAP Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                    $"fast-path=exact-unanchored, final={(candidate is null ? 0 : 1)}, " +
                    $"findWall={findStopwatch.ElapsedMilliseconds} ms, findCpu={findCpu.TotalMilliseconds:F0} ms, " +
                    $"elapsed={stopwatch.ElapsedMilliseconds} ms, cpu={(process.TotalProcessorTime - cpuStarted).TotalMilliseconds:F0} ms.");
            }

            return candidate is null
                ? TargetResolution<AutomationElement>.NotFound()
                : TargetResolution<AutomationElement>.Resolved(candidate);
        }

        // When a target is constrained by both an ancestor/context and a descendant,
        // invert the search: locate the narrow ancestor scope first, then the identifying
        // descendant, and walk upward to the primary target. This avoids enumerating every
        // primary candidate and issuing one descendant UIA query per candidate.
        if (TryResolveFromAnchors(root, descriptor, out var anchorFirstCandidates)
            && anchorFirstCandidates.Count > 0)
        {
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds >= 100)
            {
                Console.Error.WriteLine(
                    $"[DAP Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                    $"fast-path=anchor-first, final={anchorFirstCandidates.Count}, " +
                    $"elapsed={stopwatch.ElapsedMilliseconds} ms, cpu={(process.TotalProcessorTime - cpuStarted).TotalMilliseconds:F0} ms.");
            }

            return anchorFirstCandidates.Count switch
            {
                1 => TargetResolution<AutomationElement>.Resolved(anchorFirstCandidates[0]),
                _ => TargetResolution<AutomationElement>.Ambiguous(anchorFirstCandidates.Count)
            };
        }

        var primaryFindStopwatch = Stopwatch.StartNew();
        var primaryFindCpuStarted = process.TotalProcessorTime;
        var candidates = Find(root, descriptor.Locator).ToList();
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
                $"[DAP Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
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

    private static bool TryResolveFromAnchors(
        AutomationElement root,
        TargetDescriptor descriptor,
        out List<AutomationElement> candidates)
    {
        candidates = [];

        var scopeAnchor = descriptor.Anchors.FirstOrDefault(anchor =>
            anchor.Relation is AnchorRelation.Ancestor or AnchorRelation.Context
            && IsExactLocator(anchor.Locator));
        var descendantAnchor = descriptor.Anchors.FirstOrDefault(anchor =>
            anchor.Relation == AnchorRelation.Descendant
            && IsExactLocator(anchor.Locator));

        if (scopeAnchor is null || descendantAnchor is null)
            return false;

        // Find all declared scopes so duplicate containers still preserve normal
        // ambiguity semantics instead of silently choosing the first one.
        var scopes = Find(root, scopeAnchor.Locator).ToList();
        if (scopes.Count == 0)
            return false;

        foreach (var scope in scopes)
        {
            foreach (var descendant in Find(scope, descendantAnchor.Locator))
            {
                var current = TreeWalker.ControlViewWalker.GetParent(descendant);
                while (current is not null && !Automation.Compare(current, scope))
                {
                    if (MatchesLocator(current, descriptor.Locator)
                        && MatchesAnchors(current, descriptor.Anchors)
                        && !candidates.Any(existing => Automation.Compare(existing, current)))
                    {
                        candidates.Add(current);
                    }

                    current = TreeWalker.ControlViewWalker.GetParent(current);
                }
            }
        }

        return true;
    }

    private static bool IsExactLocator(Locator locator) =>
        TryCreateNativeCondition(locator, out _);

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
            var regex = new Regex(locator.Value, RegexOptions.CultureInvariant);
            return root.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(element => regex.IsMatch(element.Current.Name ?? string.Empty))
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
        return Equals(actual, property.Value);
    }
}
