using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TheArtOfDev.HtmlRenderer.WPF;

namespace GuideMe.Runtime.Windows.Bubbles;

/// <summary>
/// Browser-free HTML content for both Windows learner bubble variants.
/// Formatting is rendered by HtmlRenderer.WPF; the floating window, interaction,
/// and placement remain owned by WPF. No WebView2 or hand-built WPF inline parser.
/// </summary>
internal static class WindowsBubbleHtmlView
{
    internal static HtmlLabel Create(string? html)
    {
        var view = new HtmlLabel
        {
            Width = 260,
            MaxWidth = 260,
            Background = Brushes.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsHitTestVisible = false,
            Focusable = false
        };
        SetContent(view, html);
        return view;
    }

    internal static void SetContent(HtmlLabel view, string? html)
    {
        var safeContent = Sanitize(html ?? string.Empty);
        try
        {
            view.Text = "<div style=\"text-align:right;" +
                        "font-family:Segoe UI,Arial;font-size:15px;line-height:1.45;" +
                        "font-weight:600;color:#1f2d3d;overflow-wrap:break-word;\">" +
                        safeContent + "</div>";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GuideMe Windows bubble HTML] Render error: {ex}");
            // Bad formatting must not make a learner step impossible to read.
            view.Text = "<div style=\"text-align:right\">" +
                        WebUtility.HtmlEncode(Regex.Replace(html ?? string.Empty, "<[^>]*>", string.Empty)) +
                        "</div>";
        }
    }

    private static string Sanitize(string html)
    {
        // The Instructor permits a deliberately small HTML vocabulary.
        // Reject executable markup, attributes and any remote resource loads.
        html = Regex.Replace(html,
            @"<\s*(script|style|iframe|object|embed|svg|math|template|form)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>",
            string.Empty, RegexOptions.IgnoreCase);
        var output = new StringBuilder();
        var lists = new Stack<(string Kind, int Number)>();
        foreach (var part in Regex.Split(html, @"(<[^>]*>)"))
        {
            if (part.Length == 0) continue;
            if (!part.StartsWith('<'))
            {
                // Decode exactly once. Do not reinterpret encoded markup.
                output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(part)));
                continue;
            }
            var tag = Regex.Match(part,
                @"^<\s*(/?)\s*(strong|b|em|i|u|br|ul|ol|li|p|div|span)\b[^>]*>$",
                RegexOptions.IgnoreCase);
            if (!tag.Success) continue;
            var closing = tag.Groups[1].Value == "/";
            var name = tag.Groups[2].Value.ToLowerInvariant();
            if (name == "br")
            {
                output.Append("<br>");
                continue;
            }
            if (name is "ul" or "ol")
            {
                if (closing)
                {
                    if (lists.Count > 0) lists.Pop();
                }
                else lists.Push((name, 0));
                continue;
            }
            if (name == "li")
            {
                if (closing) output.Append("</div>");
                else
                {
                    var marker = "•";
                    if (lists.Count > 0)
                    {
                        var list = lists.Pop();
                        list.Number++;
                        marker = list.Kind == "ol" ? list.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : "•";
                        lists.Push(list);
                    }
                    output.Append("<div style=\"text-align:right;margin:2px 0;\">")
                          .Append("&#x200F;").Append(WebUtility.HtmlEncode(marker))
                          .Append("&#x00A0;&#x00A0;");
                }
                continue;
            }
            if (closing)
            {
                output.Append("</").Append(name).Append('>');
                continue;
            }
            if (name == "span")
            {
                var style = Regex.Match(part, "style\\s*=\\s*[\"']([^\"']*)[\"']",
                    RegexOptions.IgnoreCase);
                var safeStyle = new StringBuilder();
                if (style.Success)
                {
                    foreach (var declaration in style.Groups[1].Value.Split(';'))
                    {
                        var pair = declaration.Split(':', 2);
                        if (pair.Length != 2) continue;
                        var key = pair[0].Trim().ToLowerInvariant();
                        var color = pair[1].Trim();
                        if (key is not ("color" or "background-color") ||
                            !Regex.IsMatch(color, @"^#[0-9a-fA-F]{6}$")) continue;
                        safeStyle.Append(key).Append(':').Append(color).Append(';');
                    }
                }
                output.Append(safeStyle.Length == 0
                    ? "<span>"
                    : "<span style=\"" + safeStyle + "\">");
                continue;
            }
            output.Append('<').Append(name).Append('>');
        }
        return output.ToString();
    }
}
