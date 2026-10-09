using System.Diagnostics;
using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.Runtime.Windows.Targets;

public sealed class WindowsApplicationContextResolver
{
    public AutomationElement Resolve(GuideApplicationContext context)
    {
        if (context.Runtime != TargetRuntime.Windows || context.Matchers.Count == 0)
            throw new InvalidOperationException("Invalid Windows application context.");

        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        var matches = new List<AutomationElement>();
        foreach (AutomationElement window in windows)
        {
            try
            {
                if (context.Matchers.All(m => Match(window, m)))
                    matches.Add(window);
            }
            catch (ElementNotAvailableException) { }
        }
        if (matches.Count != 1)
            throw new InvalidOperationException($"Application context '{context.Key}' matched {matches.Count} windows; expected exactly one.");
        return matches[0];
    }

    private static bool Match(AutomationElement window, ApplicationContextMatcher matcher)
    {
        if (string.IsNullOrWhiteSpace(matcher.Value))
            throw new InvalidOperationException("Empty application matcher.");
        return matcher.Kind switch
        {
            "WindowTitleContains" => window.Current.Name.Contains(matcher.Value, StringComparison.OrdinalIgnoreCase),
            "AutomationId" => window.Current.AutomationId == matcher.Value,
            "ProcessName" => Process.GetProcessById(window.Current.ProcessId).ProcessName.Equals(matcher.Value, StringComparison.OrdinalIgnoreCase),
            _ => throw new NotSupportedException($"Unsupported Windows matcher '{matcher.Kind}'.")
        };
    }
}
