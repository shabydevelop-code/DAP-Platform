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

        var candidates = Find(root, descriptor.Locator).ToList();
        if (descriptor.Anchors.Count > 0)
            candidates = candidates.Where(candidate => MatchesAnchors(candidate, descriptor.Anchors)).ToList();

        return candidates.Count switch
        {
            0 => TargetResolution<AutomationElement>.NotFound(),
            1 => TargetResolution<AutomationElement>.Resolved(candidates[0]),
            _ => TargetResolution<AutomationElement>.Ambiguous(candidates.Count)
        };
    }

    private static IEnumerable<AutomationElement> Find(AutomationElement root, Locator locator)
    {
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

    private static bool MatchesAnchor(AutomationElement candidate, Anchor anchor)
    {
        var condition = CreateCondition(anchor.Locator);
        return anchor.Relation switch
        {
            AnchorRelation.Ancestor or AnchorRelation.Context =>
                HasInParentChain(candidate, condition),
            AnchorRelation.Descendant =>
                candidate.FindFirst(TreeScope.Descendants, condition) is not null,
            AnchorRelation.Sibling =>
                HasSibling(candidate, condition),
            AnchorRelation.Nearby =>
                HasSibling(candidate, condition) || HasInParentChain(candidate, condition),
            _ => false
        };
    }

    private static bool HasInParentChain(AutomationElement candidate, Condition condition)
    {
        var walker = TreeWalker.ControlViewWalker;
        for (var current = walker.GetParent(candidate); current is not null; current = walker.GetParent(current))
            if (MatchesCondition(current, condition))
                return true;
        return false;
    }

    private static bool HasSibling(AutomationElement candidate, Condition condition)
    {
        var walker = TreeWalker.ControlViewWalker;
        var parent = walker.GetParent(candidate);
        if (parent is null)
            return false;

        foreach (AutomationElement sibling in parent.FindAll(TreeScope.Children, condition))
            if (!Automation.Compare(candidate, sibling))
                return true;

        return false;
    }

    private static bool MatchesCondition(AutomationElement element, Condition condition)
    {
        if (condition is not PropertyCondition property)
            return false;

        var actual = element.GetCurrentPropertyValue(property.Property, true);
        return Equals(actual, property.Value);
    }
}
