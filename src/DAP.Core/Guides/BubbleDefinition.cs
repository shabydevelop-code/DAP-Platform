namespace DAP.Core.Guides;

public enum BubblePlacement
{
    Auto,
    Top,
    Right,
    Bottom,
    Left
}

public sealed record BubbleDefinition(
    string Content,
    BubblePlacement Placement = BubblePlacement.Auto);
