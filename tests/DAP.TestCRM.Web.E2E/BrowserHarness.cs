using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DAP.TestCRM.Web.E2E;

internal sealed class BrowserHarness : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _profileDirectory;
    private readonly CdpConnection _cdp;

    private BrowserHarness(Process process, string profileDirectory, CdpConnection cdp, BrowserPage page)
    {
        _process = process;
        _profileDirectory = profileDirectory;
        _cdp = cdp;
        Page = page;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public BrowserPage Page { get; }
    public event EventHandler? Disconnected;
    public bool HasExited => _process.HasExited;

    public static async Task<BrowserHarness> LaunchAsync(
        string browserName,
        int debuggingPort,
        string extensionDirectory,
        string initialUrl = "about:blank",
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveBrowserExecutable(browserName);
        var profileDirectory = Path.Combine(
            Path.GetTempPath(), "DAP", "E2E", "BrowserProfiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDirectory);

        var arguments = string.Join(" ", new[]
        {
            "--new-window",
            "--start-maximized",
            $"--remote-debugging-port={debuggingPort}",
            $"--user-data-dir=\"{profileDirectory}\"",
            $"--disable-extensions-except=\"{extensionDirectory}\"",
            $"--load-extension=\"{extensionDirectory}\"",
            "--no-first-run",
            "--no-default-browser-check",
            initialUrl
        });

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = false
        }) ?? throw new InvalidOperationException($"Could not launch browser '{browserName}'.");

        try
        {
            using var http = new HttpClient();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            string? webSocketUrl = null;

            while (DateTime.UtcNow < deadline && webSocketUrl is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException($"Browser '{browserName}' exited before CDP became ready.");

                try
                {
                    var targets = await http.GetFromJsonAsync<List<CdpTarget>>(
                        $"http://127.0.0.1:{debuggingPort}/json/list", cancellationToken);
                    webSocketUrl = targets?
                        .FirstOrDefault(t => t.Type == "page" && !t.Url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                        ?.WebSocketDebuggerUrl;
                }
                catch (HttpRequestException) { }
                catch (JsonException) { }

                if (webSocketUrl is null)
                    await Task.Delay(100, cancellationToken);
            }

            if (webSocketUrl is null)
                throw new TimeoutException("Browser CDP page endpoint did not become ready within 10 seconds.");

            var cdp = await CdpConnection.ConnectAsync(webSocketUrl, cancellationToken);
            await cdp.SendAsync("Page.enable", null, cancellationToken);
            await cdp.SendAsync("Runtime.enable", null, cancellationToken);
            await cdp.SendAsync("Network.enable", null, cancellationToken);

            var page = new BrowserPage(cdp);
            return new BrowserHarness(process, profileDirectory, cdp, page);
        }
        catch
        {
            TryKill(process);
            TryDeleteDirectory(profileDirectory);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cdp.DisposeAsync();
        TryKill(_process);
        _process.Dispose();
        TryDeleteDirectory(_profileDirectory);
    }

    private static string ResolveBrowserExecutable(string browserName)
    {
        browserName = browserName.Trim().ToLowerInvariant();

        IEnumerable<string> ChromeCandidates()
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(pfx86, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe");
        }

        IEnumerable<string> EdgeCandidates()
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Path.Combine(pfx86, "Microsoft", "Edge", "Application", "msedge.exe");
            yield return Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe");
        }

        IEnumerable<string> ChromiumCandidates()
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, "Chromium", "Application", "chrome.exe");
            yield return Path.Combine(pf, "Chromium", "Application", "chrome.exe");
            yield return Path.Combine(pfx86, "Chromium", "Application", "chrome.exe");
        }

        var candidates = browserName switch
        {
            "chrome" => ChromeCandidates(),
            "edge" => EdgeCandidates(),
            "chromium" => ChromiumCandidates().Concat(ChromeCandidates()),
            _ => throw new ArgumentException(
                $"Unsupported DAP_E2E_BROWSER '{browserName}'. Supported values: chromium, chrome, edge.")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"Could not find an installed executable for DAP_E2E_BROWSER='{browserName}'.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private sealed record CdpTarget(
        [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url,
        [property: System.Text.Json.Serialization.JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);
}

internal sealed class BrowserPage
{
    private readonly CdpConnection _cdp;
    private readonly List<string> _initScripts = new();
    private readonly Dictionary<string, BrowserFrame> _knownFrames = new(StringComparer.Ordinal);
    private TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);

    public BrowserPage(CdpConnection cdp)
    {
        _cdp = cdp;
        MainFrame = new BrowserFrame(this, null, "");
    }

    public BrowserFrame MainFrame { get; }
    public BrowserKeyboard Keyboard => new(this);
    public BrowserMouse Mouse => new(this);
    public BrowserViewport ViewportSize => GetViewportAsync().GetAwaiter().GetResult();
    public IReadOnlyList<BrowserFrame> Frames
    {
        get
        {
            RefreshKnownFramesAsync().GetAwaiter().GetResult();
            return new[] { MainFrame }.Concat(_knownFrames.Values).ToArray();
        }
    }

    internal BrowserFrame ActiveFrame { get; set; } = null!;

    public void SetDefaultTimeout(int milliseconds) => _defaultTimeout = TimeSpan.FromMilliseconds(milliseconds);
    internal TimeSpan DefaultTimeout => _defaultTimeout;

    public BrowserLocator Locator(string selector) => MainFrame.Locator(selector);

    public async Task AddInitScriptAsync(string script)
    {
        _initScripts.Add(script);
        await _cdp.SendAsync("Page.addScriptToEvaluateOnNewDocument", new { source = script });
    }

    public Task WaitForTimeoutAsync(int milliseconds) => Task.Delay(milliseconds);

    public async Task SetExtraHttpHeadersAsync(Dictionary<string, string> headers)
        => await _cdp.SendAsync("Network.setExtraHTTPHeaders", new { headers });

    public async Task GotoAsync(string url)
    {
        await _cdp.SendAsync("Page.navigate", new { url });
        await WaitForAsync(
            "document.readyState === 'interactive' || document.readyState === 'complete'",
            _defaultTimeout);
        foreach (var script in _initScripts)
            await EvaluateAsync<object?>($"() => {{ {script} }}");
        await RefreshKnownFramesAsync();
    }

    public Task<T> EvaluateAsync<T>(string script) => MainFrame.EvaluateAsync<T>(script);

    public async Task<BrowserFrame?> FindFrameByNameAsync(string name)
    {
        await RefreshKnownFramesAsync();
        return _knownFrames.TryGetValue(name, out var frame) ? frame : null;
    }

    internal async Task<string> EvaluateRawAsync(string expression)
    {
        var result = await _cdp.SendAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true,
            userGesture = true
        });

        if (result.TryGetProperty("exceptionDetails", out var exception))
            throw new BrowserHarnessException(exception.ToString());

        var value = result.GetProperty("result");
        if (!value.TryGetProperty("value", out var actual))
            return "null";
        return actual.GetRawText();
    }

    internal async Task<T> EvaluateExpressionAsync<T>(string expression)
    {
        var raw = await EvaluateRawAsync(expression);
        if (typeof(T) == typeof(object))
            return default!;
        return JsonSerializer.Deserialize<T>(raw, JsonOptions) !;
    }

    internal async Task WaitForAsync(string conditionExpression, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await EvaluateExpressionAsync<bool>($"Boolean({conditionExpression})"))
                    return;
            }
            catch (Exception ex) { last = ex; }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Browser condition timed out after {timeout.TotalSeconds:0.###} seconds. {last?.Message}");
    }

    internal async Task DispatchMouseMoveAsync(double x, double y)
        => await _cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });

    internal async Task DispatchWheelAsync(double x, double y, double deltaX, double deltaY)
        => await _cdp.SendAsync("Input.dispatchMouseEvent", new
        {
            type = "mouseWheel", x, y, deltaX, deltaY
        });

    internal async Task<BrowserViewport> GetViewportAsync()
        => await EvaluateAsync<BrowserViewport>("() => ({ Width: innerWidth, Height: innerHeight })");

    private async Task RefreshKnownFramesAsync()
    {
        BrowserFrameInfo[] frames;
        try
        {
            frames = await EvaluateAsync<BrowserFrameInfo[]>(
                @"() => Array.from(document.querySelectorAll('iframe,frame')).map((f,i)=>({
                    Name:f.getAttribute('name')||f.id||('frame-'+i),
                    Selector:f.id ? '#'+CSS.escape(f.id) : (f.getAttribute('name') ? 'iframe[name='+JSON.stringify(f.getAttribute('name'))+']' : 'iframe:nth-of-type('+(i+1)+')'),
                    Url:(()=>{try{return f.contentWindow.location.href}catch{return f.src||''}})()
                }))");
        }
        catch
        {
            return;
        }

        foreach (var info in frames)
        {
            if (!_knownFrames.TryGetValue(info.Name, out var frame))
                _knownFrames[info.Name] = frame = new BrowserFrame(this, info.Selector, info.Name);
            frame.Url = info.Url ?? string.Empty;
            frame.IsDetached = false;
        }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record BrowserFrameInfo(string Name, string Selector, string? Url);
}

internal sealed class BrowserFrame
{
    private readonly BrowserPage _page;
    private readonly string? _frameSelector;

    public BrowserFrame(BrowserPage page, string? frameSelector, string name)
    {
        _page = page;
        _frameSelector = frameSelector;
        Name = name;
        _page.ActiveFrame ??= this;
    }

    public string Name { get; }
    public string Url { get; internal set; } = string.Empty;
    public bool IsDetached { get; internal set; }

    internal string DocumentExpression => _frameSelector is null
        ? "document"
        : $"document.querySelector({Js(_frameSelector)})?.contentDocument";

    internal string WindowExpression => _frameSelector is null
        ? "window"
        : $"document.querySelector({Js(_frameSelector)})?.contentWindow";

    public BrowserLocator Locator(string selector) => new(this, selector);

    public async Task<T> EvaluateAsync<T>(string script)
    {
        var normalized = script.Trim();
        var expression = _frameSelector is null
            ? $"({normalized})()"
            : $"(()=>{{const __w={WindowExpression}; if(!__w) throw new Error('Frame unavailable'); return __w.eval({Js("(")} + {Js(normalized)} + {Js(")()")});}})()";
        return await _page.EvaluateExpressionAsync<T>(expression);
    }

    public async Task RefreshUrlAsync()
    {
        Url = await _page.EvaluateExpressionAsync<string>(
            $"(()=>{{const w={WindowExpression}; return w ? w.location.href : '';}})()");
    }

    internal BrowserPage Page => _page;
    internal static string Js(string value) => JsonSerializer.Serialize(value);
}

internal sealed class BrowserLocator
{
    private readonly BrowserFrame _frame;
    private readonly string _selector;
    private readonly int? _index;
    private readonly BrowserLocator? _parent;

    public BrowserLocator(BrowserFrame frame, string selector, int? index = null, BrowserLocator? parent = null)
    {
        _frame = frame;
        _selector = selector;
        _index = index;
        _parent = parent;
    }

    public BrowserLocator First => new(_frame, _selector, 0, _parent);
    public BrowserLocator Locator(string selector) => new(_frame, selector, null, this);

    private string ElementsExpression
    {
        get
        {
            var root = _parent is null
                ? _frame.DocumentExpression
                : $"({ _parent.ElementExpression })";
            var selector = _selector;
            var text = ExtractHasText(ref selector);
            var baseExpr = $"Array.from(({root})?.querySelectorAll({BrowserFrame.Js(selector)})||[])";
            if (text is not null)
                baseExpr += $".filter(e=>(e.textContent||'').includes({BrowserFrame.Js(text)}))";
            return baseExpr;
        }
    }

    private string ElementExpression => _index is int i
        ? $"({ElementsExpression})[{i}]"
        : $"({ElementsExpression})[0]";

    public async Task<int> CountAsync()
        => await _frame.Page.EvaluateExpressionAsync<int>($"({ElementsExpression}).length");

    public async Task<string?> GetAttributeAsync(string name)
        => await _frame.Page.EvaluateExpressionAsync<string?>(
            $"(()=>{{const e={ElementExpression}; return e ? e.getAttribute({BrowserFrame.Js(name)}) : null;}})()");

    public async Task<string?> TextContentAsync()
        => await _frame.Page.EvaluateExpressionAsync<string?>(
            $"(()=>{{const e={ElementExpression}; return e ? e.textContent : null;}})()");

    public async Task<string> InputValueAsync()
        => await _frame.Page.EvaluateExpressionAsync<string>(
            $"(()=>{{const e={ElementExpression}; return e && 'value' in e ? String(e.value??'') : '';}})()");

    public async Task<bool> IsVisibleAsync()
        => await _frame.Page.EvaluateExpressionAsync<bool>(
            $"(()=>{{const e={ElementExpression}; if(!e) return false; const r=e.getBoundingClientRect(); const w=e.ownerDocument.defaultView; const s=w.getComputedStyle(e); return r.width>0&&r.height>0&&s.visibility!=='hidden'&&s.display!=='none';}})()");

    public async Task<bool> IsDisabledAsync()
        => await _frame.Page.EvaluateExpressionAsync<bool>(
            $"(()=>{{const e={ElementExpression}; return !!e && (e.disabled===true || e.getAttribute?.('aria-disabled')==='true');}})()");

    public async Task ScrollIntoViewIfNeededAsync()
        => await _frame.Page.EvaluateRawAsync(
            $"(()=>{{const e={ElementExpression}; if(!e) return null; const r=e.getBoundingClientRect(); const w=e.ownerDocument.defaultView; if(!(r.top>=0&&r.left>=0&&r.bottom<=w.innerHeight&&r.right<=w.innerWidth)) e.scrollIntoView({{block:'center',inline:'nearest'}}); return null;}})()");

    public async Task<BrowserBox?> BoundingBoxAsync()
    {
        var local = await _frame.Page.EvaluateExpressionAsync<BrowserBox?>(
            $"(()=>{{const e={ElementExpression}; if(!e) return null; const r=e.getBoundingClientRect(); return {{X:r.x,Y:r.y,Width:r.width,Height:r.height}};}})()");
        if (local is null || _frame.Name.Length == 0)
            return local;

        var frameBox = await _frame.Page.MainFrame.Locator(
            _frame.Name == "dap-header" ? "iframe[name='dap-header']" :
            _frame.Name == "dap-content" ? "#content-frame" :
            $"iframe[name='{EscapeCssAttribute(_frame.Name)}']").BoundingBoxAsync();
        return frameBox is null
            ? local
            : local with { X = local.X + frameBox.X, Y = local.Y + frameBox.Y };
    }

    public async Task WaitForAsync(BrowserWaitOptions? options = null)
    {
        options ??= new BrowserWaitOptions();
        var timeout = TimeSpan.FromMilliseconds(options.Timeout ?? _frame.Page.DefaultTimeout.TotalMilliseconds);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var count = await CountAsync();
            var visible = count > 0 && await IsVisibleAsync();
            var satisfied = options.State switch
            {
                BrowserWaitState.Attached => count > 0,
                BrowserWaitState.Detached => count == 0,
                BrowserWaitState.Hidden => count == 0 || !visible,
                _ => visible
            };
            if (satisfied) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Locator '{_selector}' did not reach state {options.State}.");
    }

    public async Task ClickAsync()
    {
        _frame.Page.ActiveFrame = _frame;
        await _frame.Page.EvaluateRawAsync(
            $"(()=>{{const e={ElementExpression}; if(!e) throw new Error('Target not found'); e.focus?.(); e.click(); return null;}})()");
    }

    public async Task HoverAsync(BrowserHoverOptions? options = null)
    {
        var box = await BoundingBoxAsync() ?? throw new BrowserHarnessException("Target has no bounding box.");
        var x = box.X + (options?.Position?.X ?? box.Width / 2);
        var y = box.Y + (options?.Position?.Y ?? box.Height / 2);
        await _frame.Page.DispatchMouseMoveAsync(x, y);
    }

    public async Task SelectOptionAsync(string value)
    {
        _frame.Page.ActiveFrame = _frame;
        await _frame.Page.EvaluateRawAsync(
            $"(()=>{{const e={ElementExpression}; if(!e) throw new Error('Target not found'); e.value={BrowserFrame.Js(value)}; e.dispatchEvent(new Event('input',{{bubbles:true}})); e.dispatchEvent(new Event('change',{{bubbles:true}})); return null;}})()");
    }

    public async Task<T> EvaluateAsync<T>(string script)
    {
        var normalized = script.Trim();
        var expression =
            $"(()=>{{const el={ElementExpression}; if(!el) throw new Error('Target not found'); return ({normalized})(el);}})()";
        return await _frame.Page.EvaluateExpressionAsync<T>(expression);
    }

    private static string? ExtractHasText(ref string selector)
    {
        var marker = ":has-text(";
        var index = selector.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return null;
        var start = index + marker.Length;
        if (start >= selector.Length) return null;
        var quote = selector[start];
        if (quote is not ('\'' or '"')) return null;
        var end = selector.IndexOf(quote, start + 1);
        if (end < 0) return null;
        var text = selector[(start + 1)..end];
        var close = selector.IndexOf(')', end + 1);
        if (close < 0) return null;
        selector = selector.Remove(index, close - index + 1);
        return text;
    }

    private static string EscapeCssAttribute(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");
}

internal sealed class BrowserKeyboard
{
    private readonly BrowserPage _page;
    public BrowserKeyboard(BrowserPage page) => _page = page;

    public async Task PressAsync(string key)
    {
        var frame = _page.ActiveFrame ?? _page.MainFrame;
        if (key.Equals("Control+A", StringComparison.OrdinalIgnoreCase))
        {
            await _page.EvaluateRawAsync(
                $"(()=>{{const d={frame.DocumentExpression}; const e=d?.activeElement; e?.select?.(); return null;}})()");
            return;
        }

        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase))
        {
            await _page.EvaluateRawAsync(
                $"(()=>{{const d={frame.DocumentExpression}; const e=d?.activeElement; e?.blur?.(); return null;}})()");
            return;
        }

        await _page.EvaluateRawAsync(
            $"(()=>{{const d={frame.DocumentExpression}; const e=d?.activeElement; if(!e) return null; e.dispatchEvent(new KeyboardEvent('keydown',{{key:{BrowserFrame.Js(key)},bubbles:true}})); e.dispatchEvent(new KeyboardEvent('keyup',{{key:{BrowserFrame.Js(key)},bubbles:true}})); return null;}})()");
    }

    public async Task TypeAsync(string value)
    {
        var frame = _page.ActiveFrame ?? _page.MainFrame;
        await _page.EvaluateRawAsync(
            $"(()=>{{const d={frame.DocumentExpression}; const e=d?.activeElement; if(!e||!('value' in e)) throw new Error('No active text editor'); const start=typeof e.selectionStart==='number'?e.selectionStart:0; const end=typeof e.selectionEnd==='number'?e.selectionEnd:start; e.value=String(e.value||'').slice(0,start)+{BrowserFrame.Js(value)}+String(e.value||'').slice(end); e.dispatchEvent(new Event('input',{{bubbles:true}})); return null;}})()");
    }
}

internal sealed class BrowserMouse
{
    private readonly BrowserPage _page;
    public BrowserMouse(BrowserPage page) => _page = page;

    public async Task WheelAsync(double deltaX, double deltaY)
    {
        var viewport = await _page.GetViewportAsync();
        await _page.DispatchWheelAsync(viewport.Width / 2, viewport.Height / 2, deltaX, deltaY);
    }
}

internal sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Task _receiveLoop;
    private int _nextId;

    private CdpConnection(ClientWebSocket socket)
    {
        _socket = socket;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public static async Task<CdpConnection> ConnectAsync(string webSocketUrl, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);
        return new CdpConnection(socket);
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(stream.ToArray());
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idElement)) continue;
                var id = idElement.GetInt32();
                if (!_pending.TryGetValue(id, out var completion)) continue;

                if (root.TryGetProperty("error", out var error))
                    completion.TrySetException(new BrowserHarnessException(error.ToString()));
                else if (root.TryGetProperty("result", out var value))
                    completion.TrySetResult(value.Clone());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "DAP E2E complete", CancellationToken.None);
        }
        catch { }
        _socket.Dispose();
        _cts.Dispose();
        try { await _receiveLoop; } catch { }
    }
}

internal sealed class BrowserHarnessException : Exception
{
    public BrowserHarnessException(string message) : base(message) { }
}

internal sealed record BrowserBox(double X, double Y, double Width, double Height);
internal sealed class BrowserPoint
{
    public double X { get; init; }
    public double Y { get; init; }
}
internal sealed record BrowserViewport(double Width, double Height);
internal sealed class BrowserHoverOptions
{
    public BrowserPoint? Position { get; init; }
}
internal sealed class BrowserWaitOptions
{
    public BrowserWaitState State { get; init; } = BrowserWaitState.Visible;
    public double? Timeout { get; init; }
}
internal enum BrowserWaitState { Visible, Hidden, Attached, Detached }
