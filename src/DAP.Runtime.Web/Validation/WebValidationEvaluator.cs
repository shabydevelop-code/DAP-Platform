using DAP.Core.Guides;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Validation;

public sealed class WebValidationEvaluator
{
    public async Task<bool> IsSatisfiedAsync(
        ILocator target,
        ValidationDefinition validation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return validation.Kind switch
        {
            "value-not-empty" => !string.IsNullOrWhiteSpace(await target.InputValueAsync()),
            _ => throw new NotSupportedException($"Unsupported Web validation kind '{validation.Kind}'.")
        };
    }
}
