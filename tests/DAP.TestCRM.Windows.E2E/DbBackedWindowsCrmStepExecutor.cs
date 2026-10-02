using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.Windows.E2E;

internal sealed class DbBackedWindowsCrmStepExecutor
{
    private readonly AutomationElement _window;

    public DbBackedWindowsCrmStepExecutor(AutomationElement window)
    {
        _window = window;
    }

    public async Task RunAsync(IReadOnlyList<GuideStep> steps)
    {
        foreach (var step in steps.OrderBy(x => x.Order))
        {
            Console.WriteLine($"Windows DB-backed CRM Step {step.Order}/{steps.Count}: {step.Id}");
            Execute(step);
            await Task.Delay(100);
        }
    }

    private void Execute(GuideStep step)
    {
        if (step.Target is null || step.Target.Runtime != TargetRuntime.Windows)
            throw new InvalidOperationException($"Step '{step.Id}' must have a Windows target.");

        if (step.Target.Anchors.Count != 0)
            throw new NotSupportedException($"DB-backed CRM proof does not yet support anchors for Step '{step.Id}'.");

        var target = Resolve(step.Target.Locator);
        var validation = step.Validation
            ?? throw new InvalidOperationException($"Step '{step.Id}' needs validation so the test action can be derived.");

        switch (validation.Kind)
        {
            case "value-equals":
                if (validation.ExpectedValue is null)
                    throw new InvalidOperationException($"Step '{step.Id}' value-equals validation has no expected value.");
                if (!target.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
                    throw new InvalidOperationException($"Step '{step.Id}' target does not expose ValuePattern.");
                ((ValuePattern)valuePattern).SetValue(validation.ExpectedValue);
                WaitForValue(step.Target.Locator, validation.ExpectedValue);
                break;

            case "clicked":
                if (!target.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern))
                    throw new InvalidOperationException($"Step '{step.Id}' target does not expose InvokePattern.");
                ((InvokePattern)invokePattern).Invoke();
                break;

            default:
                throw new NotSupportedException(
                    $"Cannot derive a CRM-only test action from validation '{validation.Kind}' for Step '{step.Id}'.");
        }
    }

    private AutomationElement Resolve(Locator locator)
    {
        Condition condition = locator.Strategy switch
        {
            "automation-id" => new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                locator.Value),
            "name" => new PropertyCondition(
                AutomationElement.NameProperty,
                locator.Value),
            _ => throw new NotSupportedException(
                $"DB-backed CRM proof does not support locator strategy '{locator.Strategy}'.")
        };

        var matches = _window.FindAll(TreeScope.Descendants, condition)
            .Cast<AutomationElement>()
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Target '{locator.Strategy}:{locator.Value}' was not found."),
            _ => throw new InvalidOperationException(
                $"Target '{locator.Strategy}:{locator.Value}' is ambiguous ({matches.Length} matches).")
        };
    }

    private void WaitForValue(Locator locator, string expectedValue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var current = Resolve(locator);
            if (current.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                && string.Equals(
                    ((ValuePattern)pattern).Current.Value,
                    expectedValue,
                    StringComparison.Ordinal))
                return;

            Thread.Sleep(100);
        }

        throw new TimeoutException(
            $"Target '{locator.Strategy}:{locator.Value}' did not reach expected value '{expectedValue}'.");
    }
}
