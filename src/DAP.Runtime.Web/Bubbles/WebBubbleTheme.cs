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
        BackgroundColor: "#ffffff",
        TextColor: "#1f2937",
        BorderColor: "#cbd5e1",
        BorderWidth: 1,
        BorderRadius: 10,
        MaxWidth: 320,
        Padding: "12px 16px",
        BoxShadow: "0 8px 24px rgba(0,0,0,.18)",
        FontFamily: "Arial,sans-serif",
        FontSize: 14,
        LineHeight: 1.4,
        Direction: "rtl",
        TargetHighlightColor: "#2563eb",
        TargetHighlightWidth: 2,
        TargetHighlightShadow: "0 0 0 3px rgba(37,99,235,.18)",
        PointerSize: 9);
}
