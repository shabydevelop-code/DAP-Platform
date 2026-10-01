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
        BackgroundColor: "#173b63",
        TextColor: "#ffffff",
        BorderColor: "#0f2f50",
        BorderWidth: 1,
        BorderRadius: 8,
        MaxWidth: 320,
        Padding: "12px 16px",
        BoxShadow: "0 10px 28px rgba(15,47,80,.30)",
        FontFamily: "Arial,sans-serif",
        FontSize: 14,
        LineHeight: 1.4,
        Direction: "rtl",
        TargetHighlightColor: "#2f80ed",
        TargetHighlightWidth: 2,
        TargetHighlightShadow: "0 0 0 3px rgba(47,128,237,.18)",
        PointerSize: 9);
}
