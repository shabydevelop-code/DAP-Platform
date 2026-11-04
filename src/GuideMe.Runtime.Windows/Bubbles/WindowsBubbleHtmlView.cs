using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GuideMe.Runtime.Windows.Bubbles;

/// <summary>
/// Displays the Instructor's small, allowlisted HTML vocabulary without converting
/// lists or text formatting into WPF Inlines. The WPF bubble owns chrome and actions.
/// </summary>
internal sealed class WindowsBubbleHtmlView : Grid
{
    private readonly WebView2CompositionControl _browser;
    private readonly TextBlock _fallback;
    private string _content = string.Empty;
    private Task? _initialization;
    private bool _ready;
    private const double ContentWidth = 260;
    private const double MaximumHeight = 420;

    internal event Action? ContentSizeChanged;

    internal WindowsBubbleHtmlView()
    {
        Width = ContentWidth;
        HorizontalAlignment = HorizontalAlignment.Right;
        FlowDirection = FlowDirection.LeftToRight;

        _fallback = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            FontSize = 15,
            Width = ContentWidth,
            Visibility = Visibility.Collapsed
        };
        _browser = new WebView2CompositionControl
        {
            Width = ContentWidth,
            Height = 26,
            DefaultBackgroundColor = System.Drawing.Color.Transparent,
            Focusable = false,
            IsHitTestVisible = false
        };
        Children.Add(_browser);
        Children.Add(_fallback);
        _browser.WebMessageReceived += (_, e) =>
        {
            if (!double.TryParse(e.TryGetWebMessageAsString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var height))
                return;
            var adjusted = Math.Clamp(Math.Ceiling(height), 24, MaximumHeight);
            if (Math.Abs(_browser.Height - adjusted) < 1) return;
            _browser.Height = adjusted;
            ContentSizeChanged?.Invoke();
        };
        _browser.NavigationStarting += (_, e) =>
        {
            if (!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        };
        Loaded += (_, _) => _ = EnsureReadyAsync();
        Unloaded += (_, _) => { };
    }

    internal void ShowHtml(string? value)
    {
        var next = value ?? string.Empty;
        if (string.Equals(_content, next, StringComparison.Ordinal) && (_ready || _initialization is not null)) return;
        _content = next;
        _fallback.Text = Regex.Replace(WebUtility.HtmlDecode(_content), "<[^>]*>", string.Empty);
        if (_ready) Navigate();
        else if (IsLoaded) _ = EnsureReadyAsync();
    }

    private async Task EnsureReadyAsync()
    {
        if (_ready) return;
        if (_initialization is not null)
        {
            await _initialization;
            return;
        }

        _initialization = InitializeCoreAsync();
        await _initialization;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            await _browser.EnsureCoreWebView2Async();
            var core = _browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            _ready = true;
            Navigate();
        }
        catch (Exception exception)
        {
            // A missing WebView2 Runtime must not hide the instructor's instructions.
            Console.Error.WriteLine($"[GuideMe Windows bubble HTML] WebView2 initialization failed: {exception}");
            _browser.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
            ContentSizeChanged?.Invoke();
        }
    }

    private void Navigate()
    {
        _browser.CoreWebView2.NavigateToString(BuildDocument(_content));
    }

    private static string BuildDocument(string raw)
    {
        var safe = SanitizeFragment(raw);
        return "<!doctype html><html lang='he' dir='rtl'><head><meta charset='utf-8'>" +
            "<meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline';\">" +
            "<style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}" +
            "body{font:600 15px/1.45 'Segoe UI',Arial,sans-serif;color:#1f2d3d;direction:rtl;text-align:right}" +
            "#content{box-sizing:border-box;width:100%;overflow-wrap:anywhere;white-space:pre-wrap}" +
            "ul,ol{direction:rtl;text-align:right;white-space:normal;margin:4px 0;padding-inline-start:0;padding-inline-end:24px}" +
            "li{padding:0;margin:2px 0}p,div{margin:0}strong,b{font-weight:700}" +
            "</style></head><body><main id='content'>" + safe + "</main>" +
            "<script>function report(){window.chrome.webview.postMessage(String(document.documentElement.scrollHeight));}" +
            "addEventListener('load',report);new ResizeObserver(report).observe(document.getElementById('content'));" +
            "report();</script></body></html>";
    }

    private static string SanitizeFragment(string value)
    {
        // Never allow executable elements, remote resources, event handlers, or
        // arbitrary CSS. Only formatting tags already produced by Instructor.
        var cleaned = Regex.Replace(value, @"<\s*(script|style|iframe|object|embed|svg|math|template)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>",
            string.Empty, RegexOptions.IgnoreCase);
        var output = new StringBuilder();
        var parts = Regex.Split(cleaned, @"(<[^>]*>)");
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (!part.StartsWith('<'))
            {
                output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(part)));
                continue;
            }
            var tag = Regex.Match(part, @"^<\s*(/?)\s*(strong|b|em|i|u|br|ul|ol|li|p|div|span)\b[^>]*>$",
                RegexOptions.IgnoreCase);
            if (!tag.Success) continue;
            var closing = tag.Groups[1].Value == "/";
            var name = tag.Groups[2].Value.ToLowerInvariant();
            if (name == "br") { output.Append("<br>"); continue; }
            if (closing) { output.Append("</").Append(name).Append('>'); continue; }
            if (name == "span")
            {
                var style = Regex.Match(part, "style\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase);
                var properties = new StringBuilder();
                if (style.Success)
                {
                    foreach (var declaration in style.Groups[1].Value.Split(';'))
                    {
                        var pair = declaration.Split(':', 2);
                        if (pair.Length != 2) continue;
                        var property = pair[0].Trim().ToLowerInvariant();
                        var color = pair[1].Trim();
                        if (property is not ("color" or "background-color") ||
                            !Regex.IsMatch(color, @"^#[0-9a-fA-F]{6}$")) continue;
                        properties.Append(property).Append(':').Append(color).Append(';');
                    }
                }
                output.Append(properties.Length > 0
                    ? "<span style=\"" + properties + "\">"
                    : "<span>");
                continue;
            }
            output.Append('<').Append(name).Append('>');
        }
        return output.ToString();
    }
}
