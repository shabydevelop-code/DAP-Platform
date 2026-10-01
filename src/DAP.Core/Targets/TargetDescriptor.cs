namespace DAP.Core.Targets;

public sealed record TargetDescriptor(
    TargetRuntime Runtime,
    Locator Locator,
    IReadOnlyList<Anchor> Anchors,
    FrameContext? FrameContext = null)
{
    public static TargetDescriptor Create(
        TargetRuntime runtime,
        Locator locator,
        IEnumerable<Anchor>? anchors = null,
        FrameContext? frameContext = null)
        => new(
            runtime,
            locator,
            anchors?.ToArray() ?? Array.Empty<Anchor>(),
            frameContext);
}
