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
            "clicked" => await ConsumeClickAsync(target),
            _ => throw new NotSupportedException($"Unsupported Web validation kind '{validation.Kind}'.")
        };
    }

    private static async Task<bool> ConsumeClickAsync(ILocator target)
    {
        const string script = """
el => {
    const state = el.ownerDocument.__dapValidationState;
    return state?.clicked === true;
}
""";
        return await target.EvaluateAsync<bool>(script);
    }
}
