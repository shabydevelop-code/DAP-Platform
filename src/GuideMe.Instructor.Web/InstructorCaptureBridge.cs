using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace GuideMe.Instructor.Web;

// Ephemeral, same-user capture session. Nothing is written to the V5 database
// until an author explicitly accepts and saves a draft (not yet implemented).
internal sealed class InstructorCaptureBridge
{
    internal const string ExtensionOrigin = "chrome-extension://icnbpbekaibdhbmlnccfcibmmboeckgp";
    private readonly object _gate = new();
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private DateTimeOffset _pairUntil;
    private DateTimeOffset _lastHeartbeat;
    private bool _recording;
    private string? _targetUrl, _targetTitle;
    private int? _tabId;
    private readonly List<JsonElement> _events = new();

    public bool PairingOpen { get { lock (_gate) return DateTimeOffset.UtcNow < _pairUntil; } }
    public bool Connected { get { lock (_gate) return DateTimeOffset.UtcNow - _lastHeartbeat < TimeSpan.FromSeconds(5); } }
    public bool Recording { get { lock (_gate) return _recording; } }
    public object Status()
    {
        lock (_gate)
            return new { connected = DateTimeOffset.UtcNow - _lastHeartbeat < TimeSpan.FromSeconds(5),
                pairingOpen = DateTimeOffset.UtcNow < _pairUntil, recording = _recording,
                targetUrl = _targetUrl, targetTitle = _targetTitle, tabId = _tabId,
                eventsCount = _events.Count };
    }
    public void OpenPairing() { lock (_gate) _pairUntil = DateTimeOffset.UtcNow.AddSeconds(30); }
    public string? Pair()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow >= _pairUntil) return null;
            _pairUntil = DateTimeOffset.MinValue;
            _lastHeartbeat = DateTimeOffset.UtcNow;
            return _token;
        }
    }
    public bool Verify(string? token)
    {
        if (token is null) return false;
        var received = System.Text.Encoding.UTF8.GetBytes(token);
        var expected = System.Text.Encoding.UTF8.GetBytes(_token);
        return CryptographicOperations.FixedTimeEquals(received, expected);
    }
    public void Heartbeat() { lock (_gate) _lastHeartbeat = DateTimeOffset.UtcNow; }
    public void SetTarget(string url, string title, int? tabId)
    {
        var uri = ParseTarget(url);
        lock (_gate)
        {
            _targetUrl = uri.GetLeftPart(UriPartial.Authority);
            _targetTitle = title[..Math.Min(title.Length, 200)];
            _tabId = tabId;
        }
    }
    public void Begin()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _lastHeartbeat >= TimeSpan.FromSeconds(5) || _tabId is null)
                throw new InvalidOperationException("יש לחבר את התוסף ולבחור לשונית יעד לפני ההקלטה.");
            _events.Clear();
            _recording = true;
        }
    }
    public JsonElement[] End()
    {
        lock (_gate) { _recording = false; return _events.ToArray(); }
    }
    public void Receive(JsonElement value)
    {
        if (!value.TryGetProperty("kind", out var k) || k.GetString() is not ("click" or "change" or "blur"))
            throw new ArgumentException("Invalid capture event.");
        if (!value.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Capture URL missing.");
        var uri = ParseTarget(u.GetString()!);
        lock (_gate)
        {
            if (!_recording || _targetUrl is null || uri.GetLeftPart(UriPartial.Authority) != _targetUrl) return;
            if (_events.Count < 200) _events.Add(value.Clone());
        }
    }
    private static Uri ParseTarget(string url)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Invalid target URL.");
        return uri;
    }
}
