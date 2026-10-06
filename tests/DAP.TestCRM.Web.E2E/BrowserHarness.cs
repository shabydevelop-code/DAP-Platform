using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DAP.TestCRM.Web.E2E;

/// <summary>
/// Web E2E browser driver that uses the exact DAP browser-extension boundary.
/// It does not use an alternate browser-control or debugging stack.
/// </summary>
internal sealed class BrowserHarness : IAsyncDisposable
{
    private const string PipeName = "dap-web-e2e-v1";
    private const string RequiredTestDriverVersion = "1.0.0";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new(StringComparer.Ordinal);
    private readonly List<NamedPipeServerStream> _pipes = new();
    private readonly SemaphoreSlim _pipeWriteLock = new(1, 1);
    private readonly SemaphoreSlim _connectedSignal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _pipeGate = new();
    private NamedPipeServerStream? _selectedPipe;
    private readonly Task _acceptLoop;
    private readonly Task _sessionMonitor;
    private int _disconnectedRaised;

    private BrowserHarness(string sessionId)
    {
        SessionId = sessionId;
        Page = new BrowserPage(this);
        _acceptLoop = Task.Run(() => AcceptPipeLoopAsync(_cts.Token));
        _sessionMonitor = Task.Run(() => MonitorSessionAsync(_cts.Token));
    }

    public string SessionId { get; }
    public BrowserPage Page { get; }
    public event EventHandler? Disconnected;

    public static async Task<BrowserHarness> LaunchAsync(
        string initialUrl,
        CancellationToken cancellationToken = default)
    {

        if (string.Equals(initialUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Extension-native Web E2E requires an application URL at browser launch.", nameof(initialUrl));

        var harness = new BrowserHarness(Guid.NewGuid().ToString("N"));
        try
        {
            var extensionId = ResolveRegisteredExtensionId();
            var browser = ResolveInstalledExtensionBrowser(extensionId);
            var sessionUrl = AddSession(initialUrl, harness.SessionId);

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = browser.ExecutablePath,
                Arguments = $"--profile-directory=\"{browser.ProfileDirectory}\" --new-window \"{sessionUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = false
            }) ?? throw new InvalidOperationException(
                $"Could not launch the browser profile containing DAP extension '{extensionId}'.");

            Console.WriteLine(
                $"Web E2E extension host: {browser.DisplayName}, profile {browser.ProfileDirectory} " +
                $"(DAP extension {extensionId})");

            using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyCts.CancelAfter(CommandTimeout);
            try
            {
                var ping = await harness.SendCommandAsync(new { type = "testPing" }, readyCts.Token);
                var result = ping.GetProperty("result");
                var driverVersion = result.TryGetProperty("testDriverVersion", out var driverVersionElement)
                    ? driverVersionElement.GetString()
                    : null;
                var extensionVersion = result.TryGetProperty("extensionVersion", out var extensionVersionElement)
                    ? extensionVersionElement.GetString()
                    : null;

                if (!string.Equals(driverVersion, RequiredTestDriverVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"DAP browser extension is stale or incompatible. " +
                        $"Required test-driver protocol {RequiredTestDriverVersion}, " +
                        $"connected protocol {driverVersion ?? "<missing>"}, " +
                        $"extension version {extensionVersion ?? "<unknown>"}. " +
                        "Reload the unpacked DAP Web Runtime extension before running Web E2E.");
                }

                Console.WriteLine(
                    $"DAP extension handshake: version {extensionVersion ?? "<unknown>"}, " +
                    $"test-driver protocol {driverVersion}.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "DAP browser extension did not connect to the Web E2E driver within 5 seconds. " +
                    "The runner selected the browser profile that contains the registered DAP extension; " +
                    "reload that extension and verify its Native Messaging host registration.");
            }

            return harness;
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    internal async Task<JsonElement> SendCommandAsync(object command, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var token = timeout.Token;
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;

        try
        {
            var line = JsonSerializer.Serialize(new
            {
                type = "testDriverCommand",
                requestId,
                sessionId = SessionId,
                command
            }, BrowserPage.JsonOptions);

            await WritePipeLineAsync(line, token);
            var response = await completion.Task.WaitAsync(token);
            if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new BrowserHarnessException(
                    response.TryGetProperty("error", out var error)
                        ? error.GetString() ?? "Extension test-driver command failed."
                        : "Extension test-driver command failed.");
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Extension test-driver command '{requestId}' timed out after {CommandTimeout.TotalSeconds:0} seconds.");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task AcceptPipeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);

                lock (_pipeGate)
                    _pipes.Add(server);
                _connectedSignal.Release();

                var connectedServer = server;
                _ = Task.Run(() => ReadPipeLoopAsync(connectedServer, cancellationToken), CancellationToken.None);
                server = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task ReadPipeLoopAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeElement) ||
                    typeElement.GetString() != "testDriverResponse")
                    continue;

                var requestId = root.TryGetProperty("requestId", out var id) ? id.GetString() : null;
                if (requestId is null ||
                    !root.TryGetProperty("response", out var response) ||
                    !_pending.TryGetValue(requestId, out var completion))
                    continue;

                var clone = response.Clone();
                var ok = clone.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();

                NamedPipeServerStream? selected;
                lock (_pipeGate) selected = _selectedPipe;

                if (selected is null)
                {
                    if (!ok) continue;
                    lock (_pipeGate)
                    {
                        _selectedPipe ??= stream;
                        selected = _selectedPipe;
                    }
                    if (!ReferenceEquals(selected, stream)) continue;
                }
                else if (!ReferenceEquals(selected, stream))
                {
                    continue;
                }

                completion.TrySetResult(clone);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            lock (_pipeGate)
            {
                _pipes.Remove(stream);
                if (ReferenceEquals(_selectedPipe, stream))
                    _selectedPipe = null;
            }
            stream.Dispose();
        }
    }

    private async Task WritePipeLineAsync(string line, CancellationToken cancellationToken)
    {
        while (true)
        {
            NamedPipeServerStream[] targets;
            lock (_pipeGate)
            {
                if (_selectedPipe is { IsConnected: true })
                    targets = new[] { _selectedPipe };
                else
                    targets = _pipes.Where(pipe => pipe.IsConnected).ToArray();
            }

            if (targets.Length == 0)
            {
                await _connectedSignal.WaitAsync(cancellationToken);
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            var wrote = false;
            await _pipeWriteLock.WaitAsync(cancellationToken);
            try
            {
                foreach (var pipe in targets)
                {
                    try
                    {
                        if (!pipe.IsConnected) continue;
                        await pipe.WriteAsync(bytes, cancellationToken);
                        await pipe.FlushAsync(cancellationToken);
                        wrote = true;
                    }
                    catch (IOException)
                    {
                        lock (_pipeGate)
                        {
                            _pipes.Remove(pipe);
                            if (ReferenceEquals(_selectedPipe, pipe))
                                _selectedPipe = null;
                        }
                    }
                }
            }
            finally
            {
                _pipeWriteLock.Release();
            }

            if (wrote) return;
        }
    }

    private async Task MonitorSessionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, cancellationToken);
                if (_selectedPipe is null) continue;
                await SendCommandAsync(new { type = "testPing" }, cancellationToken);
            }
            catch (BrowserHarnessException)
            {
                if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
                    Disconnected?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (TimeoutException)
            {
                if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
                    Disconnected?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            try { await SendCommandAsync(new { type = "testCloseTab" }, CancellationToken.None); }
            catch { }
        }

        _cts.Cancel();
        lock (_pipeGate)
        {
            foreach (var pipe in _pipes.ToArray())
                pipe.Dispose();
            _pipes.Clear();
            _selectedPipe = null;
        }

        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();

        try { await _acceptLoop; } catch { }
        try { await _sessionMonitor; } catch { }

        _pipeWriteLock.Dispose();
        _connectedSignal.Dispose();
        _cts.Dispose();
    }

    private static string AddSession(string url, string sessionId)
    {
        var builder = new UriBuilder(url);
        var query = builder.Query.TrimStart('?');
        var addition = "dap-e2e-session=" + Uri.EscapeDataString(sessionId);
        builder.Query = string.IsNullOrEmpty(query) ? addition : query + "&" + addition;
        return builder.Uri.ToString();
    }

    private static string ResolveRegisteredExtensionId()
    {
        var manifestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DAP", "NativeMessaging", "com.dap.web_runtime.json");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                "DAP Native Messaging manifest was not found. Re-run install-native-host.ps1.",
                manifestPath);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("allowed_origins", out var origins) ||
            origins.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("DAP Native Messaging manifest has no allowed_origins.");

        foreach (var originElement in origins.EnumerateArray())
        {
            var origin = originElement.GetString();
            const string prefix = "chrome-extension://";
            if (origin is null || !origin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var id = origin[prefix.Length..].TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        throw new InvalidOperationException(
            "DAP Native Messaging manifest does not contain a Chrome extension origin.");
    }

    private static BrowserLaunchTarget ResolveInstalledExtensionBrowser(string extensionId)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var installations = new[]
        {
            new BrowserInstallation(
                "Google Chrome",
                Path.Combine(local, "Google", "Chrome", "User Data"),
                new[]
                {
                    Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(pfx86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")
                }),
            new BrowserInstallation(
                "Microsoft Edge",
                Path.Combine(local, "Microsoft", "Edge", "User Data"),
                new[]
                {
                    Path.Combine(pfx86, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe")
                })
        };

        var matches = new List<BrowserLaunchTarget>();
        foreach (var installation in installations)
        {
            if (!Directory.Exists(installation.UserDataDirectory))
                continue;

            var executable = installation.ExecutableCandidates.FirstOrDefault(File.Exists);
            if (executable is null)
                continue;

            foreach (var profileDirectory in FindProfilesWithExtension(
                installation.UserDataDirectory,
                extensionId))
            {
                matches.Add(new BrowserLaunchTarget(
                    installation.DisplayName,
                    executable,
                    profileDirectory));
            }
        }

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not find an installed Chrome/Edge profile containing DAP extension '{extensionId}'. " +
                "Load or reload the unpacked DAP Web Runtime extension, then run Web E2E again.");
        }

        var descriptions = string.Join(
            ", ",
            matches.Select(match => $"{match.DisplayName}/{match.ProfileDirectory}"));
        throw new InvalidOperationException(
            $"DAP extension '{extensionId}' is enabled in multiple browser profiles: {descriptions}. " +
            "Web E2E will not guess which browser session owns the test. Keep the DAP test extension enabled " +
            "in exactly one Chrome/Edge profile while running the canonical Web E2E.");
    }

    private static IReadOnlyList<string> FindProfilesWithExtension(
        string userDataDirectory,
        string extensionId)
    {
        var matches = new List<string>();
        var profiles = Directory.EnumerateDirectories(userDataDirectory)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => string.Equals(Path.GetFileName(path), "Default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var profilePath in profiles)
        {
            var preferenceFiles = new[]
            {
                Path.Combine(profilePath, "Preferences"),
                Path.Combine(profilePath, "Secure Preferences")
            };

            var found = false;
            foreach (var preferencesPath in preferenceFiles)
            {
                if (!File.Exists(preferencesPath))
                    continue;

                try
                {
                    var jsonText = File.ReadAllText(preferencesPath);
                    using var document = JsonDocument.Parse(jsonText);
                    if (document.RootElement.TryGetProperty("extensions", out var extensions) &&
                        extensions.TryGetProperty("settings", out var settings) &&
                        settings.TryGetProperty(extensionId, out var entry))
                    {
                        if (entry.TryGetProperty("state", out var state) &&
                            state.ValueKind == JsonValueKind.Number &&
                            state.GetInt32() == 0)
                            continue;

                        found = true;
                        break;
                    }

                    if (jsonText.Contains(extensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                catch (JsonException)
                {
                    // Browser may be updating a preference file while the runner probes it.
                }
                catch (IOException)
                {
                }
            }

            if (found)
                matches.Add(Path.GetFileName(profilePath));
        }

        return matches;
    }

    private sealed record BrowserInstallation(
        string DisplayName,
        string UserDataDirectory,
        IReadOnlyList<string> ExecutableCandidates);

    private sealed record BrowserLaunchTarget(
        string DisplayName,
        string ExecutablePath,
        string ProfileDirectory);

}

internal sealed class BrowserPage
{
    private readonly BrowserHarness _driver;
    private string? _e2eMode;
    private TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);

    public BrowserPage(BrowserHarness driver)
    {
        _driver = driver;
        MainFrame = new BrowserFrame(this, "");
        ActiveFrame = MainFrame;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public BrowserFrame MainFrame { get; }
    public BrowserKeyboard Keyboard => new(this);
    public BrowserMouse Mouse => new(this);
    internal BrowserFrame ActiveFrame { get; set; }

    public BrowserViewport ViewportSize
        => GetViewportAsync().GetAwaiter().GetResult();

    public IReadOnlyList<BrowserFrame> Frames
        => GetFramesAsync().GetAwaiter().GetResult();

    public void SetDefaultTimeout(int milliseconds)
        => _defaultTimeout = TimeSpan.FromMilliseconds(milliseconds);

    internal TimeSpan DefaultTimeout => _defaultTimeout;

    public BrowserLocator Locator(string selector) => MainFrame.Locator(selector);

    public Task SetExtraHttpHeadersAsync(Dictionary<string, string> headers)
    {
        _ = headers;
        return Task.CompletedTask;
    }

    public async Task AddInitScriptAsync(string script)
    {
        var match = Regex.Match(script, @"localStorage\.setItem\('dap-e2e-mode',\s*'(?<mode>[^']+)'\)");
        if (match.Success)
            _e2eMode = match.Groups["mode"].Value;

        if (_e2eMode is not null)
        {
            await SendAsync(new
            {
                type = "testSetLocalStorage",
                frameName = "",
                key = "dap-e2e-mode",
                value = _e2eMode,
                datasetKey = "dapE2eMode"
            });
        }
    }

    public async Task GotoAsync(string url)
    {
        await SendAsync(new { type = "testNavigate", url });
        await WaitForTimeoutAsync(100);
        if (_e2eMode is not null)
        {
            for (var i = 0; i < 50; i++)
            {
                try
                {
                    await SendAsync(new
                    {
                        type = "testSetLocalStorage",
                        frameName = "",
                        key = "dap-e2e-mode",
                        value = _e2eMode,
                        datasetKey = "dapE2eMode"
                    });
                    break;
                }
                catch (BrowserHarnessException)
                {
                    await WaitForTimeoutAsync(100);
                }
            }
        }
    }

    public Task WaitForTimeoutAsync(int milliseconds) => Task.Delay(milliseconds);

    public async Task<T> EvaluateAsync<T>(string script)
    {
        if (!script.Contains("screenX", StringComparison.Ordinal))
            throw new NotSupportedException("Only browser-window metrics are exposed through the extension-native E2E API.");

        var response = await SendAsync(new { type = "testWindowMetrics", frameName = "" });
        return response.GetProperty("result").Deserialize<T>(JsonOptions)!;
    }

    public async Task<BrowserFrame?> FindFrameByNameAsync(string name)
    {
        // Chrome's webNavigation frame name reflects the browsing-context name
        // captured when the frame was created. TestCRM intentionally creates
        // dap-content-next and then promotes that same iframe element by
        // renaming it to dap-content. Resolve the live shell iframe element,
        // exactly like the production extension FrameContext path, instead of
        // trusting the stale browsing-context name.
        var selector = name switch
        {
            "dap-content" => "#content-frame",
            "dap-header" => "#header-frame",
            _ => null
        };

        if (selector is null)
            return (await GetFramesAsync())
                .FirstOrDefault(frame => string.Equals(frame.Name, name, StringComparison.Ordinal));

        try
        {
            var response = await SendAsync(new
            {
                type = "testResolveFrame",
                framePath = new[]
                {
                    new { strategy = "css", value = selector }
                }
            });
            var result = response.GetProperty("result");
            var url = result.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;
            return new BrowserFrame(this, name) { Url = url };
        }
        catch (BrowserHarnessException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<BrowserFrame>> GetFramesAsync()
    {
        var response = await SendAsync(new { type = "testGetFrames" });
        var frames = response.GetProperty("result").GetProperty("frames")
            .Deserialize<List<FrameInfo>>(JsonOptions) ?? new();
        return frames.Select(info => new BrowserFrame(this, info.Name) { Url = info.Url }).ToArray();
    }

    internal async Task<JsonElement> SendAsync(object command, CancellationToken cancellationToken = default)
        => await _driver.SendCommandAsync(command, cancellationToken);

    internal async Task<BrowserViewport> GetViewportAsync()
    {
        var response = await SendAsync(new { type = "testWindowMetrics", frameName = "" });
        var r = response.GetProperty("result");
        return new BrowserViewport(
            r.GetProperty("innerWidth").GetDouble(),
            r.GetProperty("innerHeight").GetDouble());
    }

    private sealed record FrameInfo(string Name, string Url);
}

internal sealed class BrowserFrame
{
    private readonly BrowserPage _page;

    public BrowserFrame(BrowserPage page, string name)
    {
        _page = page;
        Name = name;
    }

    public string Name { get; }
    public string Url { get; internal set; } = string.Empty;
    public bool IsDetached => false;

    public BrowserLocator Locator(string selector) => new(this, new[] { new LocatorPart(selector, null) });

    public async Task RefreshUrlAsync()
    {
        var frame = await _page.FindFrameByNameAsync(Name);
        Url = frame?.Url ?? string.Empty;
    }

    public async Task<T> EvaluateAsync<T>(string script)
    {
        if (!script.Contains("performance.timeOrigin", StringComparison.Ordinal))
            throw new NotSupportedException("Unsupported frame evaluation in extension-native E2E.");

        var response = await _page.SendAsync(new { type = "testDocumentTimeOrigin", frameName = Name });
        return response.GetProperty("result").GetProperty("value").Deserialize<T>(BrowserPage.JsonOptions)!;
    }

    public async Task EvaluateAsync(string script)
    {
        if (!script.Contains("location.reload", StringComparison.Ordinal))
            throw new NotSupportedException("Unsupported frame evaluation in extension-native E2E.");

        await _page.SendAsync(new { type = "testReload", frameName = Name });
    }

    internal BrowserPage Page => _page;
}

internal sealed record LocatorPart(string Selector, int? Index);

internal sealed class BrowserLocator
{
    private readonly BrowserFrame _frame;
    private readonly IReadOnlyList<LocatorPart> _path;

    public BrowserLocator(BrowserFrame frame, IReadOnlyList<LocatorPart> path)
    {
        _frame = frame;
        _path = path;
    }

    public BrowserLocator First
        => WithFinalIndex(0);

    public BrowserLocator Nth(int index)
        => WithFinalIndex(index);

    public BrowserLocator Locator(string selector)
        => new(_frame, _path.Concat(new[] { new LocatorPart(selector, null) }).ToArray());

    public async Task<int> CountAsync()
    {
        var result = await OpAsync("count");
        return result.GetProperty("count").GetInt32();
    }

    public async Task<string?> GetAttributeAsync(string name)
    {
        var result = await OpAsync("attribute", new { name });
        return ReadOptionalString(result);
    }

    public async Task<string?> TextContentAsync()
    {
        var result = await OpAsync("text");
        return ReadOptionalString(result);
    }

    public async Task<string> InputValueAsync()
    {
        var result = await OpAsync("inputValue");
        return ReadOptionalString(result) ?? string.Empty;
    }

    public async Task<bool> IsVisibleAsync()
    {
        var result = await OpAsync("visible");
        return ReadBool(result);
    }

    public async Task<bool> IsDisabledAsync()
    {
        var result = await OpAsync("disabled");
        return ReadBool(result);
    }

    public async Task ScrollIntoViewIfNeededAsync()
    {
        _frame.Page.ActiveFrame = _frame;
        await OpAsync("scrollIntoView");
    }

    public async Task<BrowserBox?> BoundingBoxAsync()
    {
        _frame.Page.ActiveFrame = _frame;
        var result = await OpAsync("box");
        if (!result.TryGetProperty("box", out var box))
            return null;
        return box.Deserialize<BrowserBox>(BrowserPage.JsonOptions);
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

        throw new TimeoutException($"Extension locator did not reach state {options.State}.");
    }

    public async Task ClickAsync()
    {
        _frame.Page.ActiveFrame = _frame;
        await OpAsync("click");
    }

    public async Task HoverAsync(BrowserHoverOptions? options = null)
    {
        _frame.Page.ActiveFrame = _frame;
        await OpAsync("hover", new
        {
            localX = options?.Position?.X,
            localY = options?.Position?.Y
        });
    }

    public async Task SelectOptionAsync(string value)
    {
        _frame.Page.ActiveFrame = _frame;
        await OpAsync("select", new { value });
    }

    public async Task<T> EvaluateAsync<T>(string script)
    {
        string op;
        if (script.Contains("tagName", StringComparison.Ordinal))
            op = "tagName";
        else
            throw new NotSupportedException("Unsupported locator evaluation in extension-native E2E.");

        var result = await OpAsync(op);
        return result.GetProperty("value").Deserialize<T>(BrowserPage.JsonOptions)!;
    }

    private BrowserLocator WithFinalIndex(int index)
    {
        var parts = _path.ToArray();
        var last = parts[^1];
        parts[^1] = last with { Index = index };
        return new BrowserLocator(_frame, parts);
    }

    private async Task<JsonElement> OpAsync(string op, object? extras = null)
    {
        var command = new Dictionary<string, object?>
        {
            ["type"] = "testLocator",
            ["frameName"] = _frame.Name,
            ["path"] = _path.Select(part => new { selector = part.Selector, index = part.Index }).ToArray(),
            ["op"] = op
        };

        if (extras is not null)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(extras, BrowserPage.JsonOptions));
            foreach (var property in doc.RootElement.EnumerateObject())
                command[property.Name] = property.Value.Clone();
        }

        var response = await _frame.Page.SendAsync(command);
        return response.GetProperty("result");
    }

    private static string? ReadOptionalString(JsonElement result)
        => result.TryGetProperty("value", out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement result)
        => result.TryGetProperty("value", out var value) && value.GetBoolean();
}

internal sealed class BrowserKeyboard
{
    private readonly BrowserPage _page;
    public BrowserKeyboard(BrowserPage page) => _page = page;

    public async Task PressAsync(string key)
        => await _page.SendAsync(new
        {
            type = "testKeyboard",
            frameName = _page.ActiveFrame.Name,
            key
        });

    public async Task TypeAsync(string value)
        => await _page.SendAsync(new
        {
            type = "testType",
            frameName = _page.ActiveFrame.Name,
            value
        });
}

internal sealed class BrowserMouse
{
    private readonly BrowserPage _page;
    public BrowserMouse(BrowserPage page) => _page = page;

    public async Task WheelAsync(double deltaX, double deltaY)
        => await _page.SendAsync(new
        {
            type = "testWheel",
            frameName = _page.ActiveFrame.Name,
            deltaX,
            deltaY
        });
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
