using System.Text.Json;

namespace GuideMe.Instructor.Web;

// Recording state belongs to the Web Instructor host, not to the extension UI.
internal sealed class InstructorCaptureBridge
{
    private readonly object _gate = new();
    private DateTimeOffset _lastHeartbeat;
    private bool _recording;
    private readonly List<JsonElement> _events = new();
    private List<CaptureTab> _tabs = new();
    private int? _selectedTab;
    private string? _selectedContext, _targetUrl, _targetTitle;
    private long? _guideId;

    public object Heartbeat(CaptureTab[] tabs)
    {
        if (tabs.Length > 100) throw new ArgumentException("Too many browser tabs.");
        var accepted = tabs.Where(t => t.TabId > 0 && t.Url.Length <= 4096 &&
            Uri.TryCreate(t.Url, UriKind.Absolute, out var uri) &&
            uri.Scheme is ("http" or "https") && t.Title.Length <= 200).ToList();
        lock (_gate)
        {
            _lastHeartbeat = DateTimeOffset.UtcNow;
            _tabs = accepted;
            if (_selectedTab is not null && !_tabs.Any(t => t.TabId == _selectedTab))
            {
                _selectedTab = null;
                _recording = false;
            }
            return new { selectedTabId = _selectedTab, recording = _recording };
        }
    }
    public bool IsConnected
    {
        get { lock (_gate) return DateTimeOffset.UtcNow - _lastHeartbeat < TimeSpan.FromSeconds(10); }
    }

    public CaptureTab[] GetOpenTabs()
    {
        lock (_gate)
            return DateTimeOffset.UtcNow - _lastHeartbeat < TimeSpan.FromSeconds(10)
                ? _tabs.ToArray() : Array.Empty<CaptureTab>();
    }

    public object Status()
    {
        lock (_gate)
            return new {
                connected = DateTimeOffset.UtcNow - _lastHeartbeat < TimeSpan.FromSeconds(10),
                pairingOpen = false, recording = _recording,
                guideId = _guideId, contextId = _selectedContext,
                targetUrl = _targetUrl, targetTitle = _targetTitle,
                tabId = _selectedTab, tabs = _tabs,
                eventsCount = _events.Count
            };
    }

    public object Select(long guideId, string contextId, string matchersJson, int? explicitTabId)
    {
        using var parsed = JsonDocument.Parse(matchersJson);
        var matchers = parsed.RootElement.EnumerateArray().Select(item => (
            Kind: item.GetProperty("Kind").GetString() ?? "",
            Value: item.GetProperty("Value").GetString() ?? "")).ToArray();
        if (matchers.Length == 0) throw new ArgumentException("No Web application matchers.");
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _lastHeartbeat > TimeSpan.FromSeconds(10))
                throw new InvalidOperationException("תוסף Instructor אינו מחובר.");
            var matching = _tabs.Where(tab => matchers.All(m => Match(tab, m.Kind, m.Value))).ToArray();
            if (matching.Length != 1 && explicitTabId is null)
                return new { status = matching.Length == 0 ? "notFound" : "multiple", tabs = matching };
            if (explicitTabId is not null && matching.All(t => t.TabId != explicitTabId))
                throw new ArgumentException("Selected tab does not match the saved application context.");
            var target = explicitTabId is null ? matching[0] : matching.Single(t => t.TabId == explicitTabId);
            _recording = false;
            _events.Clear();
            _guideId = guideId;
            _selectedContext = contextId;
            _selectedTab = target.TabId;
            _targetTitle = target.Title;
            _targetUrl = new Uri(target.Url).GetLeftPart(UriPartial.Authority);
            return new { status = "selected", tabs = new[] { target } };
        }
    }
    private static bool Match(CaptureTab tab, string kind, string value)
    {
        return kind.ToLowerInvariant() switch
        {
            "urlhost" => Uri.TryCreate(tab.Url, UriKind.Absolute, out var u) &&
                string.Equals(u.Authority, value, StringComparison.OrdinalIgnoreCase),
            "urlequals" => string.Equals(tab.Url, value, StringComparison.OrdinalIgnoreCase),
            "urlcontains" => tab.Url.Contains(value, StringComparison.OrdinalIgnoreCase),
            "titlecontains" => tab.Title.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
    public void Begin(long guideId, string contextId)
    {
        lock (_gate)
        {
            if (_guideId != guideId || _selectedContext != contextId ||
                _selectedTab is null || !_tabs.Any(t => t.TabId == _selectedTab))
                throw new InvalidOperationException("יש לבחור תחילה אפליקציית Web זמינה.");
            if (DateTimeOffset.UtcNow - _lastHeartbeat >= TimeSpan.FromSeconds(10))
                throw new InvalidOperationException("תוסף Instructor אינו מחובר.");
            _events.Clear();
            _recording = true;
        }
    }
    public JsonElement[] End()
    {
        lock (_gate) { _recording = false; return _events.ToArray(); }
    }
    public void Receive(CaptureEvent data)
    {
        if (data.TabId <= 0 || data.Event.ValueKind != JsonValueKind.Object ||
            !data.Event.TryGetProperty("kind", out var k) || k.GetString() is not ("click" or "change" or "blur") ||
            !data.Event.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Invalid capture event.");
        lock (_gate)
        {
            if (!_recording || _selectedTab != data.TabId || _targetUrl is null ||
                !Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri) ||
                uri.GetLeftPart(UriPartial.Authority) != _targetUrl) return;
            if (_events.Count < 200) _events.Add(data.Event.Clone());
        }
    }
}
internal sealed record CaptureTab(int TabId, string Url, string Title);
internal sealed record CaptureEvent(int TabId, JsonElement Event);
internal sealed record CaptureSelection(long GuideId, string ContextId, int? TabId);
internal sealed record CaptureStart(long GuideId, string ContextId);
