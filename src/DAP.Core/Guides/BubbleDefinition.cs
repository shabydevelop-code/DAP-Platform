namespace DAP.Core.Guides;

public enum BubblePlacement
{
    Auto,
    Top,
    Right,
    Bottom,
    Left,
    Center
}

public sealed record BubbleDefinition(
    string Content,
    BubblePlacement Placement = BubblePlacement.Auto);
