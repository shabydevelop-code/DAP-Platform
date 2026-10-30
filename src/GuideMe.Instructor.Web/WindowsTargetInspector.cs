using System.Runtime.InteropServices;
using System.Windows.Automation;

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
        var candidates = new List<Candidate>();
        foreach (var (strategy, value, property) in new[]
        {
            ("automation-id", id, AutomationElement.AutomationIdProperty),
            ("name", name, AutomationElement.NameProperty)
        })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 400) continue;
            var matches = root.FindAll(TreeScope.Descendants, new PropertyCondition(property, value));
            if (matches.Count == 1 && Automation.Compare(matches[0], selected))
                candidates.Add(new Candidate(strategy, value));
        }

        return new Inspection(name, selected.Current.ControlType.ProgrammaticName,
            processId, candidates, candidates.Count == 0
                ? "לא נמצא מזהה ייחודי לרכיב. נדרש זיהוי באמצעות עוגנים לפני שמירה."
                : null);
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
