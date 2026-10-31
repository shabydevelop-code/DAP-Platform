using System.Runtime.InteropServices;
using System.Windows.Automation;
using GuideMe.Core.Targets;
using GuideMe.Runtime.Windows.Targets;

namespace GuideMe.Instructor.Web;

// Read-only Windows UIA inspection. Never clicks, focuses, or changes the target app.
// Candidate locators must identify exactly one element within the selected window.
internal static class WindowsTargetInspector
{
    internal sealed record Candidate(string Strategy, string Value);
    internal sealed record AnchorCandidate(string Strategy, string Value, string Relation);
    internal sealed record Inspection(string Name, string ControlType, int ProcessId,
        IReadOnlyList<Candidate> Locators, string? Error,
        IReadOnlyList<AnchorCandidate>? Anchors = null);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    internal static Inspection Inspect(int processId, bool focused) => Inspect(processId, focused, null);

    internal static Inspection Inspect(int processId, bool focused, AutomationElement? capturedElement)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
            throw new ArgumentException("תהליך Windows אינו תקין.");

        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId));
        if (roots.Count != 1)
            throw new InvalidOperationException(roots.Count == 0
                ? "לא נמצא חלון פעיל של האפליקציה שנבחרה."
                : "נמצאו מספר חלונות של אותו תהליך. יש לבחור חלון חד־משמעי.");

        var root = roots[0];
        AutomationElement? selected;
        if (capturedElement is not null)
            selected = capturedElement;
        else if (focused)
            selected = AutomationElement.FocusedElement;
        else
        {
            if (!GetCursorPos(out var point))
                throw new InvalidOperationException("לא ניתן לקרוא את מיקום העכבר.");
            selected = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        }

        if (selected is null || selected.Current.ProcessId != processId || !IsDescendantOf(selected, root))
            throw new InvalidOperationException("הרכיב שנבחר אינו שייך לחלון היעד.");

        var name = selected.Current.Name ?? "";
        var id = selected.Current.AutomationId ?? "";
        var resolver = new WindowsTargetResolver();
        foreach (var (strategy, value) in new[] { ("automation-id", id), ("name", name) })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 400) continue;
            var primary = new Candidate(strategy, value);
            var locator = new Locator(strategy, value);
            var direct = resolver.Resolve(root, TargetDescriptor.Create(TargetRuntime.Windows, locator));
            var directUnique = direct.Status == TargetResolutionStatus.Resolved &&
                direct.Target is not null && Automation.Compare(direct.Target, selected);

            // Capture a stable contextual anchor even when the primary locator is
            // currently unique. A later screen can contain another element with
            // the same name or AutomationId.
            var walker = TreeWalker.ControlViewWalker;
            var parent = walker.GetParent(selected);
            if (parent is null) continue;
            var possibleAnchors = new List<AnchorCandidate>();
            foreach (AutomationElement sibling in parent.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                if (Automation.Compare(sibling, selected)) continue;
                possibleAnchors.Add(new AnchorCandidate("name", sibling.Current.Name ?? "", "Sibling"));
                possibleAnchors.Add(new AnchorCandidate("automation-id", sibling.Current.AutomationId ?? "", "Sibling"));
            }
            for (var ancestor = parent; ancestor is not null && !Automation.Compare(ancestor, root);
                 ancestor = walker.GetParent(ancestor))
            {
                possibleAnchors.Add(new AnchorCandidate("name", ancestor.Current.Name ?? "", "Ancestor"));
                possibleAnchors.Add(new AnchorCandidate("automation-id", ancestor.Current.AutomationId ?? "", "Ancestor"));
            }
            // Prefer stable ancestor IDs, then ancestor names, before siblings.
            // Sibling labels often change when the screen is refreshed.
            var orderedAnchors = possibleAnchors
                .Where(a => !string.IsNullOrWhiteSpace(a.Value) && a.Value.Length <= 400)
                .Distinct()
                .OrderBy(a => a.Relation == "Ancestor" ? 0 : 1)
                .ThenBy(a => a.Strategy == "automation-id" ? 0 : 1)
                .Take(100);
            foreach (var anchor in orderedAnchors)
            {
                if (string.IsNullOrWhiteSpace(anchor.Value) || anchor.Value.Length > 400) continue;
                var descriptor = TargetDescriptor.Create(TargetRuntime.Windows, locator,
                    new[] { new Anchor(new Locator(anchor.Strategy, anchor.Value),
                        Enum.Parse<AnchorRelation>(anchor.Relation)) });
                var resolved = resolver.Resolve(root, descriptor);
                if (resolved.Status == TargetResolutionStatus.Resolved &&
                    resolved.Target is not null && Automation.Compare(resolved.Target, selected))
                    return new Inspection(name, selected.Current.ControlType.ProgrammaticName,
                        processId, new[] { primary }, null, new[] { anchor });
            }
            if (directUnique)
                return new Inspection(name, selected.Current.ControlType.ProgrammaticName,
                    processId, new[] { primary }, null);
        }
        return new Inspection(name, selected.Current.ControlType.ProgrammaticName,
            processId, Array.Empty<Candidate>(),
            "לא נמצא זיהוי חד־משמעי לרכיב באמצעות מאפייניו ועוגני השורה.");

    }

    private static bool IsDescendantOf(AutomationElement element, AutomationElement root)
    {
        for (AutomationElement? current = element; current is not null;
             current = TreeWalker.RawViewWalker.GetParent(current))
        {
            if (Automation.Compare(current, root)) return true;
        }
        return false;
    }
}
