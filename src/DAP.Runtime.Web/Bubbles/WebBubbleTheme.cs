namespace DAP.Runtime.Web.Bubbles;

public sealed record WebBubbleTheme(
    string BackgroundColor,
    string TextColor,
    string BorderColor,
    int BorderWidth,
    int BorderRadius,
    int MaxWidth,
    string Padding,
    string BoxShadow,
    string FontFamily,
    int FontSize,
    double LineHeight,
    string Direction,
    string TargetHighlightColor,
    int TargetHighlightWidth,
    string TargetHighlightShadow,
    int PointerSize)
{
    public static WebBubbleTheme Default { get; } = new(
        BackgroundColor: "#312E5A",
        TextColor: "#FFFFFF",
        BorderColor: "#8B83C7",
        BorderWidth: 1,
        BorderRadius: 8,
        MaxWidth: 320,
        Padding: "12px 16px",
        BoxShadow: "0 10px 28px rgba(32,29,67,.28)",
        FontFamily: "Arial,sans-serif",
        FontSize: 14,
        LineHeight: 1.4,
        Direction: "rtl",
        TargetHighlightColor: "#A99FE8",
        TargetHighlightWidth: 2,
        TargetHighlightShadow: "0 0 0 3px rgba(169,159,232,.22)",
        PointerSize: 9);
}
