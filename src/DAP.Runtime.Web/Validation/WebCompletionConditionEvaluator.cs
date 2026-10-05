using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Validation;

/// <summary>Single Playwright implementation of persisted Web completion-condition semantics.</summary>
public sealed class WebCompletionConditionEvaluator
{
    private readonly WebTargetResolver _targets = new();

    public async Task<bool> AreSatisfiedAsync(IPage page, GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.CompletionConditions is null || step.CompletionConditions.Count == 0) return true;
        foreach (var condition in step.CompletionConditions)
        {
            if (condition.Target.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException($"Web Guide Step '{step.Id}' contains a non-Web completion target.");

            var r = await _targets.ResolveAsync(page, condition.Target, cancellationToken);
            var target = r.Status == TargetResolutionStatus.Resolved ? r.Target : null;
            switch (condition.Kind.Trim().ToLowerInvariant())
            {
                case "target-exists": if (target is null) return false; break;
                case "target-not-exists": if (target is not null) return false; break;
                case "target-enabled": if (target is null || !await target.IsEnabledAsync()) return false; break;
                case "value-equals":
                    if (target is null || condition.ExpectedValue is null ||
                        !string.Equals(await target.InputValueAsync(), condition.ExpectedValue, StringComparison.Ordinal))
                        return false;
                    break;
                default: throw new NotSupportedException($"Unsupported Web completion condition kind '{condition.Kind}'.");
            }
        }
        return true;
    }
}
