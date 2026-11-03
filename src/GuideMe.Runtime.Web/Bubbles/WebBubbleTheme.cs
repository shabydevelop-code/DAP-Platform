namespace GuideMe.Runtime.Web.Bubbles;

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
        BackgroundColor: "#FFFFFF",
        TextColor: "#1F2D3D",
        BorderColor: "#F4C542",
        BorderWidth: 2,
        BorderRadius: 18,
        MaxWidth: 320,
        Padding: "12px 16px",
        BoxShadow: "0 10px 28px rgba(31,45,61,.14)",
        FontFamily: "Arial,sans-serif",
        FontSize: 14,
        LineHeight: 1.4,
        Direction: "rtl",
        TargetHighlightColor: "#FFDF00",
        TargetHighlightWidth: 3,
        TargetHighlightShadow: "0 0 0 2px #111827",
        PointerSize: 0);
}
