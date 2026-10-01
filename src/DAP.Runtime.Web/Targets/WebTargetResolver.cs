using DAP.Core.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Targets;

public sealed class WebTargetResolver : ITargetResolver<IPage, ILocator>
{
    public TargetRuntime Runtime => TargetRuntime.Web;

    public async Task<TargetResolution<ILocator>> ResolveAsync(
        IPage context,
        TargetDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Runtime != TargetRuntime.Web)
            throw new ArgumentException("WebTargetResolver can resolve only Web targets.", nameof(descriptor));

        cancellationToken.ThrowIfCancellationRequested();

        var scope = await ResolveFrameAsync(context, descriptor.FrameContext, cancellationToken);
        if (scope is null)
            return TargetResolution<ILocator>.NotFound();

        var candidates = CreateLocator(scope, descriptor.Locator);
        var count = await candidates.CountAsync();
        if (count == 0)
            return TargetResolution<ILocator>.NotFound();

        var matches = new List<ILocator>();
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates.Nth(i);
            if (await MatchesAnchorsAsync(candidate, descriptor.Anchors))
                matches.Add(candidate);
        }

        return matches.Count switch
        {
            0 => TargetResolution<ILocator>.NotFound(),
            1 => TargetResolution<ILocator>.Resolved(matches[0]),
            _ => TargetResolution<ILocator>.Ambiguous(matches.Count)
        };
    }

    private static async Task<IFrame?> ResolveFrameAsync(
        IPage page,
        FrameContext? frameContext,
        CancellationToken cancellationToken)
    {
        if (frameContext is null || frameContext.Path.Count == 0)
            return page.MainFrame;

        IFrame current = page.MainFrame;
        foreach (var frameLocator in frameContext.Path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = CreateLocator(current, frameLocator);
            if (await locator.CountAsync() != 1)
                return null;

            var handle = await locator.ElementHandleAsync();
            var next = handle is null ? null : await handle.ContentFrameAsync();
            if (next is null || next.IsDetached)
                return null;

            current = next;
        }

        return current;
    }

    private static ILocator CreateLocator(IFrame frame, Locator locator) =>
        locator.Strategy.Trim().ToLowerInvariant() switch
        {
            "css" => frame.Locator(locator.Value),
            "text" => frame.GetByText(locator.Value),
            "label" => frame.GetByLabel(locator.Value),
            "role" => CreateRoleLocator(frame, locator.Value),
            _ => throw new NotSupportedException($"Unsupported Web locator strategy '{locator.Strategy}'.")
        };

    private static ILocator CreateRoleLocator(IFrame frame, string value)
    {
        if (!Enum.TryParse<AriaRole>(value, true, out var role))
            throw new ArgumentException($"Unknown ARIA role '{value}'.", nameof(value));

        return frame.GetByRole(role);
    }

    private static async Task<bool> MatchesAnchorsAsync(
        ILocator candidate,
        IReadOnlyList<Anchor> anchors)
    {
        foreach (var anchor in anchors)
        {
            var matches = anchor.Relation switch
            {
                AnchorRelation.Ancestor or AnchorRelation.Context =>
                    await HasAncestorAsync(candidate, anchor.Locator),
                AnchorRelation.Descendant =>
                    await HasDescendantAsync(candidate, anchor.Locator),
                AnchorRelation.Sibling =>
                    await HasSiblingAsync(candidate, anchor.Locator),
                AnchorRelation.Nearby =>
                    await HasNearbyAsync(candidate, anchor.Locator),
                _ => false
            };

            if (!matches)
                return false;
        }

        return true;
    }

    private static Task<bool> HasAncestorAsync(ILocator candidate, Locator anchor) =>
        EvaluateRelationAsync(candidate, anchor, "ancestor");

    private static Task<bool> HasDescendantAsync(ILocator candidate, Locator anchor) =>
        EvaluateRelationAsync(candidate, anchor, "descendant");

    private static Task<bool> HasSiblingAsync(ILocator candidate, Locator anchor) =>
        EvaluateRelationAsync(candidate, anchor, "sibling");

    private static Task<bool> HasNearbyAsync(ILocator candidate, Locator anchor) =>
        EvaluateRelationAsync(candidate, anchor, "nearby");

    private static async Task<bool> EvaluateRelationAsync(
        ILocator candidate,
        Locator anchor,
        string relation)
    {
        if (!anchor.Strategy.Equals("css", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Initial Web anchor relations support CSS anchors only.");

        return await candidate.EvaluateAsync<bool>(
            "(el, a) => {" +
            " const s=a.selector, r=a.relation;" +
            " if(r==='ancestor') return !!el.closest(s);" +
            " if(r==='descendant') return !!el.querySelector(s);" +
            " if(r==='sibling') return !!(el.parentElement && Array.from(el.parentElement.children).some(x=>x!==el && x.matches(s)));" +
            " if(r==='nearby') return !!(el.parentElement && el.parentElement.querySelector(s));" +
            " return false;" +
            "}",
            new { selector = anchor.Value, relation });
    }
}
