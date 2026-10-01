using DAP.Core.Guides;
using DAP.Core.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebStepContextGuard
{
    public async Task<bool> IsActiveAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken = default)
    {
        if (step.Context is null)
            return true;

        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var frame = await ResolveFrameAsync(page, step.Target.FrameContext, cancellationToken);
        if (frame is null)
            return false;

        return step.Context.Kind switch
        {
            "url-equals" => string.Equals(frame.Url, step.Context.Value, StringComparison.Ordinal),
            "url-contains" => frame.Url.Contains(step.Context.Value, StringComparison.Ordinal),
            "url-fragment-equals" => Uri.TryCreate(frame.Url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Fragment, step.Context.Value, StringComparison.Ordinal),
            _ => throw new NotSupportedException($"Unsupported Web Step context kind '{step.Context.Kind}'.")
        };
    }

    private static async Task<IFrame?> ResolveFrameAsync(
        IPage page,
        FrameContext? frameContext,
        CancellationToken cancellationToken)
    {
        IFrame current = page.MainFrame;
        if (frameContext is null || frameContext.Path.Count == 0)
            return current;

        foreach (var frameLocator in frameContext.Path)
        {
            if (!string.Equals(frameLocator.Strategy, "css", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Unsupported Web frame locator strategy '{frameLocator.Strategy}'.");

            var locator = current.Locator(frameLocator.Value);
            if (await locator.CountAsync() != 1)
                return null;

            var handle = await locator.ElementHandleAsync();
            current = handle is null ? null! : await handle.ContentFrameAsync()!;
            if (current is null || current.IsDetached)
                return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return current;
    }
}
