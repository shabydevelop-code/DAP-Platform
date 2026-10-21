using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GuideMe.Instructor;

/// <summary>
/// Loopback-only capture transport. It does not interact with the production
/// Native Host or persist a guide. A separate browser capture client is required.
/// </summary>
internal sealed class BrowserCaptureServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _token = Guid.NewGuid().ToString("N");
    private readonly object _sync = new();
    private readonly List<BrowserCaptureEvent> _events = new();
    private CancellationTokenSource? _shutdown;
    private bool _recording;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private string? _activeWebUrl;
    private BrowserTargetSelection? _selectedBrowserTarget;
    public BrowserTargetSelection? SelectedBrowserTarget { get { lock (_sync) return _selectedBrowserTarget; } }
    public void ClearSelectedBrowserTarget() { lock (_sync) _selectedBrowserTarget = null; }
    private string? _activeWebTitle;
    private DateTimeOffset _activeWebReportedAt;
    public string? ActiveWebTitle { get { lock (_sync) return _activeWebTitle; } }
    public DateTimeOffset ActiveWebReportedAt { get { lock (_sync) return _activeWebReportedAt; } }
    public string? ActiveWebUrl { get { lock (_sync) return _activeWebUrl; } }
    public bool IsConnected { get { lock (_sync) return DateTimeOffset.UtcNow - _lastHeartbeat < TimeSpan.FromSeconds(5); } }

    public const int Port = 53921;
    public string SessionToken => _token;
    public event Action<BrowserCaptureEvent>? EventCaptured;

    public void Start()
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _shutdown = new CancellationTokenSource();
        _ = AcceptLoopAsync(_shutdown.Token);
    }

    public void BeginStep()
    {
        lock (_sync)
        {
            _events.Clear();
            _recording = true;
        }
    }

    public IReadOnlyList<BrowserCaptureEvent> EndStep()
    {
        lock (_sync)
        {
            _recording = false;
            return _events.ToArray();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) when (token.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            _ = HandleAsync(ctx);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            if (!IPAddress.IsLoopback(ctx.Request.RemoteEndPoint?.Address ?? IPAddress.None))
            {
                ctx.Response.StatusCode = 403;
                return;
            }
            // Bootstrap is available only to an extension origin and only during
            // an explicit, short-lived pairing window opened in Instructor.
            if (ctx.Request.HttpMethod == "OPTIONS" && ctx.Request.Url?.AbsolutePath == "/pair")
            {
                var origin = ctx.Request.Headers["Origin"];
                if (!IsExtensionOrigin(origin)) { ctx.Response.StatusCode = 403; return; }
                ctx.Response.Headers["Access-Control-Allow-Origin"] = origin!;
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "POST";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                ctx.Response.StatusCode = 204;
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/pair")
            {
                var origin = ctx.Request.Headers["Origin"];
                if (!IsExtensionOrigin(origin)) { ctx.Response.StatusCode = 403; return; }
                ctx.Response.Headers["Access-Control-Allow-Origin"] = origin!;
                ctx.Response.ContentType = "application/json";
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { token = _token }));
                await ctx.Response.OutputStream.WriteAsync(bytes);
                return;
            }
            // The session token is not exposed to websites. A future extension
            // bridge must obtain it from the Instructor's explicit pairing flow.
            if (!string.Equals(ctx.Request.Headers["X-GuideMe-Capture-Token"], _token, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 403;
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/heartbeat")
            {
                lock (_sync) _lastHeartbeat = DateTimeOffset.UtcNow;
                ctx.Response.StatusCode = 200;
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/select-target")
            {
                if (ctx.Request.ContentLength64 < 0 || ctx.Request.ContentLength64 > 2048)
                {
                    ctx.Response.StatusCode = 413;
                    return;
                }
                using var selectionDocument = await JsonDocument.ParseAsync(ctx.Request.InputStream);
                var selectionRoot = selectionDocument.RootElement;
                if (!selectionRoot.TryGetProperty("url", out var selectionUrl) ||
                    !Uri.TryCreate(selectionUrl.GetString(), UriKind.Absolute, out var selectedUri) ||
                    selectedUri.Scheme is not ("http" or "https") ||
                    !selectionRoot.TryGetProperty("tabId", out var tabIdElement) ||
                    !tabIdElement.TryGetInt32(out var tabId) ||
                    !selectionRoot.TryGetProperty("windowId", out var windowIdElement) ||
                    !windowIdElement.TryGetInt32(out var windowId))
                {
                    ctx.Response.StatusCode = 400;
                    return;
                }
                var selectionTitle = selectionRoot.TryGetProperty("title", out var titleElement) &&
                    titleElement.ValueKind == JsonValueKind.String ? titleElement.GetString() ?? "" : "";
                lock (_sync)
                    _selectedBrowserTarget = new BrowserTargetSelection(
                        selectedUri.Authority, selectionTitle, tabId, windowId, DateTimeOffset.UtcNow);
                ctx.Response.StatusCode = 204;
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/target")
            {
                if (ctx.Request.ContentLength64 < 0 || ctx.Request.ContentLength64 > 2048)
                {
                    ctx.Response.StatusCode = 413;
                    return;
                }
                using var targetDocument = await JsonDocument.ParseAsync(ctx.Request.InputStream);
                var targetRoot = targetDocument.RootElement;
                if (!targetRoot.TryGetProperty("url", out var targetUrl) ||
                    targetUrl.ValueKind != JsonValueKind.String ||
                    !Uri.TryCreate(targetUrl.GetString(), UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    ctx.Response.StatusCode = 400;
                    return;
                }
                var title = targetRoot.TryGetProperty("title", out var targetTitle) && targetTitle.ValueKind == JsonValueKind.String
                    ? targetTitle.GetString() : null;
                lock (_sync)
                {
                    _activeWebUrl = uri.GetLeftPart(UriPartial.Authority);
                    _activeWebTitle = title;
                    _activeWebReportedAt = DateTimeOffset.UtcNow;
                }
                ctx.Response.StatusCode = 204;
                return;
            }
            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/status")
            {
                ctx.Response.StatusCode = 200;
                return;
            }
            if (ctx.Request.HttpMethod != "POST" || ctx.Request.Url?.AbsolutePath != "/event")
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            if (ctx.Request.ContentLength64 < 0 || ctx.Request.ContentLength64 > 16_384)
            {
                ctx.Response.StatusCode = 413;
                return;
            }
            using var document = await JsonDocument.ParseAsync(ctx.Request.InputStream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("kind", out var kind) ||
                kind.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("url", out var url) ||
                url.ValueKind != JsonValueKind.String)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            string ReadOptional(string name)
            {
                if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
                    return "";
                return (property.GetString() ?? "")[..Math.Min(property.GetString()?.Length ?? 0, 160)];
            }
            string ReadSnapshot(string name)
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
                    return "";
                return value.GetRawText();
            }
            var captured = new BrowserCaptureEvent(
                DateTimeOffset.UtcNow, kind.GetString() ?? "", url.GetString() ?? "",
                ReadOptional("tag"), ReadOptional("label"), ReadOptional("id"),
                ReadSnapshot("before"), ReadSnapshot("after"), ReadOptional("value"),
                ReadOptional("name"), ReadOptional("frameSelector"), ReadOptional("contextCandidate"));
            bool accepted;
            lock (_sync)
            {
                accepted = _recording && _events.Count < 500;
                if (accepted) _events.Add(captured);
            }
            if (accepted) EventCaptured?.Invoke(captured);
            ctx.Response.StatusCode = accepted ? 202 : 409;
        }
        catch (JsonException) { ctx.Response.StatusCode = 400; }
        catch (Exception) { ctx.Response.StatusCode = 500; }
        finally { ctx.Response.Close(); }
    }

    private static bool IsExtensionOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        return string.Equals(origin, "chrome-extension://icnbpbekaibdhbmlnccfcibmmboeckgp", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _shutdown?.Cancel();
        _listener.Close();
        _shutdown?.Dispose();
    }
}

internal sealed record BrowserCaptureEvent(DateTimeOffset Timestamp, string Kind, string Url, string Tag, string Label, string ElementId, string Before, string After, string Value, string Name, string FrameSelector, string ContextCandidate);
\ninternal sealed record BrowserTargetSelection(string Authority, string Title, int TabId, int WindowId, DateTimeOffset SelectedAt);\n