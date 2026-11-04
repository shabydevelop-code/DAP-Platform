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
    internal static StackPanel Create(string? html)
    {
        var panel = new StackPanel
        {
            Width = 260,
            MaxWidth = 260,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        SetContent(panel, html);
        return panel;
    }

    internal static void SetContent(StackPanel panel, string? html)
    {
        panel.Children.Clear();
        var safe = Sanitize(html ?? string.Empty);
        var pieces = Regex.Split(safe, @"(<\s*/?\s*(?:ul|ol|li)\b[^>]*>)",
            RegexOptions.IgnoreCase);
        var paragraph = new StringBuilder();
        var item = new StringBuilder();
        var lists = new Stack<(string Kind, int Number)>();
        var inItem = false;

        HtmlLabel MakeText(string markup)
        {
            var label = new HtmlLabel
            {
                Width = 260,
                MaxWidth = 260,
                Background = Brushes.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsHitTestVisible = false,
                Focusable = false
            };
            try
            {
                label.Text = "<div style=\"text-align:right;font-family:Segoe UI,Arial;" +
                             "font-size:15px;line-height:1.45;font-weight:600;color:#1f2d3d;\">" +
                             markup + "</div>";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GuideMe Windows bubble HTML] Render error: {ex}");
                label.Text = WebUtility.HtmlEncode(Regex.Replace(markup, "<[^>]*>", string.Empty));
            }
            return label;
        }

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            panel.Children.Add(MakeText(paragraph.ToString()));
            paragraph.Clear();
        }

        void FlushItem()
        {
            if (!inItem) return;
            var marker = "•";
            if (lists.Count > 0)
            {
                var list = lists.Pop();
                list.Number++;
                marker = list.Kind == "ol"
                    ? list.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                    : "•";
                lists.Push(list);
            }
            var row = new Grid { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bullet = new TextBlock
            {
                Text = marker,
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Right,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 45, 61))
            };
            Grid.SetColumn(bullet, 0);
            var text = MakeText(item.ToString());
            text.Width = 238;
            text.MaxWidth = 238;
            Grid.SetColumn(text, 1);
            row.Children.Add(bullet);
            row.Children.Add(text);
            panel.Children.Add(row);
            item.Clear();
            inItem = false;
        }

        foreach (var part in pieces)
        {
            var tag = Regex.Match(part, @"^<\s*(/?)\s*(ul|ol|li)\b[^>]*>$", RegexOptions.IgnoreCase);
            if (!tag.Success)
            {
                if (inItem) item.Append(part);
                else paragraph.Append(part);
                continue;
            }
            var closing = tag.Groups[1].Value == "/";
            var name = tag.Groups[2].Value.ToLowerInvariant();
            if (name is "ul" or "ol")
            {
                if (closing)
                {
                    FlushItem();
                    if (lists.Count > 0) lists.Pop();
                }
                else
                {
                    FlushParagraph();
                    lists.Push((name, 0));
                }
            }
            else if (closing) FlushItem();
            else
            {
                FlushParagraph();
                FlushItem();
                inItem = true;
            }
        }
        FlushItem();
        FlushParagraph();
    }

    private static string Sanitize(string html)
    {
        // The Instructor permits a deliberately small HTML vocabulary.
        // Reject executable markup, attributes and any remote resource loads.
        html = Regex.Replace(html,
            @"<\s*(script|style|iframe|object|embed|svg|math|template|form)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>",
            string.Empty, RegexOptions.IgnoreCase);
        var output = new StringBuilder();
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
