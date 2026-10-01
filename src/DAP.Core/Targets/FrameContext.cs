namespace DAP.Core.Targets;

public sealed record FrameContext(
    IReadOnlyList<Locator> Path)
{
    public static FrameContext TopLevel { get; } = new(Array.Empty<Locator>());
}
