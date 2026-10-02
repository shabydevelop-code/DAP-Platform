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

        // An unanchored exact locator is the Guide's declaration that the locator itself is
        // sufficient to identify the target. Use FindFirst so UIA can stop walking large
        // descendant trees (for example, a screen containing a large DataGrid).
        if (descriptor.Anchors.Count == 0 && IsExactLocator(descriptor.Locator))
        {
            var candidate = FindFirst(root, descriptor.Locator);
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds >= 100)
            {
                Console.Error.WriteLine(
                    $"[DAP Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                    $"fast-path=exact-unanchored, final={(candidate is null ? 0 : 1)}, " +
                    $"elapsed={stopwatch.ElapsedMilliseconds} ms.");
            }

            return candidate is null
                ? TargetResolution<AutomationElement>.NotFound()
                : TargetResolution<AutomationElement>.Resolved(candidate);
        }

        var candidates = Find(root, descriptor.Locator).ToList();
        var primaryCandidateCount = candidates.Count;
        var anchorDiagnostics = new List<string>(descriptor.Anchors.Count);

        foreach (var anchor in descriptor.Anchors)
        {
            var before = candidates.Count;
            candidates = candidates.Where(candidate => MatchesAnchor(candidate, anchor)).ToList();
            anchorDiagnostics.Add(
                $"{anchor.Relation}:{anchor.Locator.Strategy}='{anchor.Locator.Value}' {before}->{candidates.Count}");
        }

        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds >= 100 || primaryCandidateCount >= 100)
        {
            var anchors = anchorDiagnostics.Count == 0
                ? "none"
                : string.Join(", ", anchorDiagnostics);
            Console.Error.WriteLine(
                $"[DAP Windows resolver timing] locator={descriptor.Locator.Strategy}='{descriptor.Locator.Value}', " +
                $"primary={primaryCandidateCount}, anchors=[{anchors}], final={candidates.Count}, " +
                $"elapsed={stopwatch.ElapsedMilliseconds} ms.");
        }

        return candidates.Count switch
        {
            0 => TargetResolution<AutomationElement>.NotFound(),
            1 => TargetResolution<AutomationElement>.Resolved(candidates[0]),
            _ => TargetResolution<AutomationElement>.Ambiguous(candidates.Count)
        };
    }

    private static bool IsExactLocator(Locator locator) =>
        locator.Strategy.Trim().Equals("automation-id", StringComparison.OrdinalIgnoreCase)
        || locator.Strategy.Trim().Equals("name", StringComparison.OrdinalIgnoreCase);

    private static AutomationElement? FindFirst(AutomationElement root, Locator locator) =>
        root.FindFirst(TreeScope.Descendants, CreateCondition(locator));

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
        return locator.Strategy.Trim().ToLowerInvariant() switch
        {
            "automation-id" => new PropertyCondition(AutomationElement.AutomationIdProperty, locator.Value),
            "name" => new PropertyCondition(AutomationElement.NameProperty, locator.Value),
            "control-type" => new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ParseControlType(locator.Value)),
            _ => throw new NotSupportedException(
                $"Unsupported Windows locator strategy '{locator.Strategy}'.")
        };
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
                candidate.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>()
                    .Any(element => MatchesLocator(element, anchor.Locator)),
            AnchorRelation.Sibling =>
                HasSibling(candidate, anchor.Locator),
            AnchorRelation.Nearby =>
                HasSibling(candidate, anchor.Locator) || HasInParentChain(candidate, anchor.Locator),
            _ => false
        };

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
