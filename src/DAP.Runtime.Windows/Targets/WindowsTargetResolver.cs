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
        if (descriptor.Anchors.Count == 0 && IsUniqueExactLocator(descriptor.Locator))
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
        if (TryResolveFromAnchors(root, descriptor, out var anchorFirstCandidates))
        {
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds >= 100)
            {
                Console.Error.WriteLine(
                    $"[DAP Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                    $"fast-path=anchor-first, final={anchorFirstCandidates.Count}, " +
                    $"elapsed={stopwatch.ElapsedMilliseconds} ms, cpu={(process.TotalProcessorTime - cpuStarted).TotalMilliseconds:F0} ms.");
            }

            // If the descriptor qualifies for anchor-first resolution, that strategy is
            // authoritative for this attempt. A temporary miss means NotFound and should
            // be retried by the runtime; do not fall through to the expensive broad scan.
            return anchorFirstCandidates.Count switch
            {
                0 => TargetResolution<AutomationElement>.NotFound(),
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
            && IsNativeExactLocator(anchor.Locator));
        var descendantAnchor = descriptor.Anchors.FirstOrDefault(anchor =>
            anchor.Relation == AnchorRelation.Descendant
            && IsExactRegexLocator(anchor.Locator));

        if (scopeAnchor is null || descendantAnchor is null)
            return false;

        if (!TryCreateNativeCondition(descendantAnchor.Locator, out var descendantCondition))
            return false;

        // Resolve the declared scope first, then ask that provider for the first exact
        // descendant match. Searching from the desktop/window root can miss descendants
        // exposed lazily by virtualized controls, while a scope-local query lets the
        // owning provider materialize its own subtree without enumerating every DataItem.
        var scope = FindFirst(root, scopeAnchor.Locator);
        if (scope is null)
            return true;

        var descendant = scope.FindFirst(TreeScope.Descendants, descendantCondition);
        if (descendant is not null)
        {
            AddPrimaryAncestorCandidate(
                descendant,
                scope,
                descriptor,
                scopeAnchor,
                descendantAnchor,
                candidates);
            return true;
        }

        // A virtualized container can report its full logical item set while exposing only
        // realized rows/cells in the UIA tree. Ask the provider for its logical items and
        // realize them one at a time until the selective descendant anchor becomes available.
        // This avoids repeatedly enumerating every DataItem and running a descendant query
        // against every row on every reconciliation pass.
        TryResolveVirtualizedItem(
            scope,
            descendantCondition,
            descriptor,
            scopeAnchor,
            descendantAnchor,
            candidates);

        return true;
    }

    private static void AddPrimaryAncestorCandidate(
        AutomationElement descendant,
        AutomationElement scope,
        TargetDescriptor descriptor,
        Anchor scopeAnchor,
        Anchor descendantAnchor,
        ICollection<AutomationElement> candidates)
    {
        var walker = TreeWalker.RawViewWalker;
        for (var current = walker.GetParent(descendant);
             current is not null && !Automation.Compare(current, scope);
             current = walker.GetParent(current))
        {
            if (!MatchesLocator(current, descriptor.Locator))
                continue;

            if (MatchesRemainingAnchors(current, descriptor.Anchors, scopeAnchor, descendantAnchor))
                candidates.Add(current);

            break;
        }
    }

    private static void TryResolveVirtualizedItem(
        AutomationElement scope,
        Condition descendantCondition,
        TargetDescriptor descriptor,
        Anchor scopeAnchor,
        Anchor descendantAnchor,
        ICollection<AutomationElement> candidates)
    {
        if (!scope.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var rawPattern)
            || rawPattern is not ItemContainerPattern itemContainer)
            return;

        var stopwatch = Stopwatch.StartNew();
        var visited = 0;
        var realized = 0;
        AutomationElement? item = null;

        try
        {
            while (visited < 10_000)
            {
                item = itemContainer.FindItemByProperty(item, null!, null!);
                if (item is null)
                    break;

                visited++;

                try
                {
                    if (item.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var rawVirtualized)
                        && rawVirtualized is VirtualizedItemPattern virtualized)
                    {
                        virtualized.Realize();
                        realized++;
                    }

                    AutomationElement? descendant = null;
                    if (MatchesCondition(item, descendantCondition))
                    {
                        descendant = item;
                    }
                    else
                    {
                        descendant = item.FindFirst(TreeScope.Descendants, descendantCondition);
                    }

                    if (descendant is null)
                        continue;

                    if (MatchesLocator(item, descriptor.Locator)
                        && MatchesRemainingAnchors(item, descriptor.Anchors, scopeAnchor, descendantAnchor))
                    {
                        candidates.Add(item);
                    }
                    else
                    {
                        AddPrimaryAncestorCandidate(
                            descendant,
                            scope,
                            descriptor,
                            scopeAnchor,
                            descendantAnchor,
                            candidates);
                    }

                    if (candidates.Count > 0)
                        break;
                }
                catch (ElementNotAvailableException)
                {
                    // Realizing a virtualized item can replace its placeholder element.
                    // Continue with the provider's logical-item enumeration.
                }
                catch (InvalidOperationException)
                {
                    // Some providers expose ItemContainerPattern but do not allow all
                    // operations on every placeholder. Continue to the next logical item.
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        stopwatch.Stop();
        Console.Error.WriteLine(
            $"[DAP Windows resolver virtualized] visited={visited}, realized={realized}, " +
            $"final={candidates.Count}, elapsed={stopwatch.ElapsedMilliseconds} ms.");
    }

    private static AutomationElement? FindRawAncestor(AutomationElement candidate, Locator locator)
    {
        var walker = TreeWalker.RawViewWalker;
        for (AutomationElement? current = candidate; current is not null; current = walker.GetParent(current))
            if (MatchesLocator(current, locator))
                return current;

        return null;
    }

    private static bool MatchesRemainingAnchors(
        AutomationElement candidate,
        IReadOnlyList<Anchor> anchors,
        Anchor scopeAnchor,
        Anchor descendantAnchor) =>
        anchors
            .Where(anchor => !ReferenceEquals(anchor, scopeAnchor) && !ReferenceEquals(anchor, descendantAnchor))
            .All(anchor => MatchesAnchor(candidate, anchor));

    private static bool HasRawAncestorOrSelf(AutomationElement candidate, AutomationElement ancestor)
    {
        var walker = TreeWalker.RawViewWalker;
        for (AutomationElement? current = candidate; current is not null; current = walker.GetParent(current))
            if (Automation.Compare(current, ancestor))
                return true;
        return false;
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
