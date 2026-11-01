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

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    // Native Win32 popup menus (#32768) can live outside the application's
    // UIA window subtree. Check the HWND owner chain rather than UIA ancestry
    // alone; never accept an unrelated foreground application.
    internal static bool BelongsToProcess(AutomationElement element, int processId)
    {
        try
        {
            for (var current = element; current is not null;
                 current = TreeWalker.RawViewWalker.GetParent(current))
            {
                if (current.Current.ProcessId == processId &&
                    current.Current.NativeWindowHandle != 0)
                    return true;
            }
            if (!GetCursorPos(out var point)) return false;
            var hwnd = GetAncestor(WindowFromPoint(point), 2); // GA_ROOT
            for (var i = 0; hwnd != 0 && i < 16; i++, hwnd = GetWindow(hwnd, 4)) // GW_OWNER
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == processId) return true;
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        return false;
    }

    internal static Inspection Inspect(int processId, bool focused) => Inspect(processId, focused, null);

    internal static Inspection Inspect(int processId, bool focused, AutomationElement? capturedElement)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
            throw new ArgumentException("תהליך Windows אינו תקין.");

        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId));
        if (roots.Count == 0)
            throw new InvalidOperationException("לא נמצא חלון פעיל של האפליקציה שנבחרה.");

        // A menu popup can add another top-level UIA window. Determine the
        // owning root from the captured element instead of assuming one window.
        AutomationElement? root = roots.Count == 1 ? roots[0] : null;
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

        if (selected is null)
            throw new InvalidOperationException("לא נבחר רכיב Windows.");

        // Popup menus can be separate top-level UIA windows of the same process.
        // Resolve against the selected popup itself, never against an unrelated app.
        if (root is null || !IsDescendantOf(selected, root))
        {
            var matchingRoots = roots.Cast<AutomationElement>()
                .Where(candidate => IsDescendantOf(selected, candidate)).Take(2).ToArray();
            if (matchingRoots.Length == 1)
                root = matchingRoots[0];
            else if (matchingRoots.Length == 0 && BelongsToProcess(selected, processId))
            {
                // A native menu can be represented as a separate UIA tree.
                // Resolve against its actual top-level ancestor.
                root = selected;
                var walker = TreeWalker.RawViewWalker;
                for (var parent = walker.GetParent(root); parent is not null &&
                     !Automation.Compare(parent, AutomationElement.RootElement);
                     parent = walker.GetParent(root))
                    root = parent;
            }
            else
                throw new InvalidOperationException("לא נמצא חלון תפריט חד־משמעי של האפליקציה.");
        }

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
