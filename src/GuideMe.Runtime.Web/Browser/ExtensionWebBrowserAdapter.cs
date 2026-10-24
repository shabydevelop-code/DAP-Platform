using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuideMe.Core.Guides;
using GuideMe.Core.Localization;
using GuideMe.Core.Targets;

namespace GuideMe.Runtime.Web.Browser;

/// <summary>
/// Production Web adapter boundary for the browser extension.
/// Guide behavior remains owned by GuideMe Runtime; browser access is routed through
/// Native Messaging hosts connected to the GuideMe named-pipe transport.
/// </summary>
public sealed class ExtensionWebBrowserAdapter : IWebBrowserAdapter, IDisposable
{
    private static readonly string PipeName = "guideMe-web-runtime-v1-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private static readonly object PresentationDiagnosticGate = new();
    private static readonly string PresentationDiagnosticPath = Path.Combine(Path.GetTempPath(), "GuideMe-web-presentation-diagnostic.log");
    private static readonly Dictionary<string, (string Status, DateTimeOffset LoggedAt)> PresentationDiagnosticStates = new(StringComparer.Ordinal);

    private static void TracePresentation(GuideStep step, string stage, string status, JsonElement? response = null)
    {
        var key = step.Id + ":" + stage;
        var now = DateTimeOffset.UtcNow;
        lock (PresentationDiagnosticGate)
        {
            if (PresentationDiagnosticStates.TryGetValue(key, out var previous) &&
                previous.Status == status && now - previous.LoggedAt < TimeSpan.FromSeconds(5))
                return;
            PresentationDiagnosticStates[key] = (status, now);
            try
            {
                var framePath = JsonSerializer.Serialize(step.Target?.FrameContext?.Path);
                var detail = response.HasValue ? response.Value.GetRawText() : "-";
                File.AppendAllText(PresentationDiagnosticPath,
                    $"{now:O} step={step.Id} stage={stage} status={status} framePath={framePath} response={detail}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingResponses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pendingErrors = new(StringComparer.Ordinal);
    // Only transition-based rules use this state; legacy target-exists is unchanged.
    private readonly Dictionary<string, (bool InitialVisible, bool SawHidden,
        string? DocumentToken, long Revision)> _completionTransitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<WebValidationCommit>> _commits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _armedValidationIds = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _pipeWriteLock = new(1, 1);
    private readonly SemaphoreSlim _connectedSignal = new(0);
    private readonly CancellationTokenSource _transportCts = new();
    private readonly object _pipeGate = new();
    private readonly object _validationGate = new();
    private readonly IUiTextProvider? _texts;
    private readonly string? _sessionId;
    private readonly string _runtimeInstanceId = Guid.NewGuid().ToString("N");
    private bool _useApplicationContexts;
    private string? _activeApplicationContextKey;
    private GuideApplicationContext? _activeApplicationContext;
    private readonly List<NamedPipeServerStream> _pipes = new();
    private NamedPipeServerStream? _selectedPipe;
    private readonly Task _acceptLoop;
    private TaskCompletionSource<bool>? _centeredDismissal;
    private bool _disposed;
    public event Action? TargetTabClosed;
    public event Action? StopRequested;
    public event Action<string>? TargetWindowActivationRequested;

    public ExtensionWebBrowserAdapter(
        string? eventPath = null,
        string? commandPath = null,
        string? responsePath = null)
        : this(null, eventPath, commandPath, responsePath)
    {
    }

    public ExtensionWebBrowserAdapter(
        IUiTextProvider? texts,
        string? eventPath = null,
        string? commandPath = null,
        string? responsePath = null)
    {
        _texts = texts;
        _sessionId = Environment.GetEnvironmentVariable("GuideMe_WEB_SESSION_ID");
        _acceptLoop = Task.Run(() => AcceptPipeLoopAsync(_transportCts.Token));
        _ = _acceptLoop.ContinueWith(
            task => Console.Error.WriteLine(
                $"[GuideMe runtime] Web pipe accept loop faulted: {task.Exception?.GetBaseException()}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
    private IReadOnlyDictionary<string, GuideApplicationContext> _applicationContexts =
        new Dictionary<string, GuideApplicationContext>(StringComparer.Ordinal);

    public void ConfigureApplicationContexts(IReadOnlyList<GuideApplicationContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        _applicationContexts = contexts.ToDictionary(context => context.Key, StringComparer.Ordinal);
        _useApplicationContexts = _applicationContexts.Count > 0;
    }

    private GuideApplicationContext? ApplicationContextFor(GuideStep step)
    {
        if (string.IsNullOrWhiteSpace(step.ApplicationContextKey)) return null;
        if (!_applicationContexts.TryGetValue(step.ApplicationContextKey, out var context))
            throw new InvalidOperationException($"Guide Step '{step.Id}' references unknown Application Context '{step.ApplicationContextKey}'.");
        if (context.Runtime != TargetRuntime.Web)
            throw new InvalidOperationException($"Guide Step '{step.Id}' references non-Web Application Context '{context.Key}'.");
        return context;
    }

    private object ContextualCommand(GuideStep step, object command)
    {
        var context = ApplicationContextFor(step);
        _activeApplicationContextKey = step.ApplicationContextKey;
        _activeApplicationContext = context;
        return new
        {
            type = "contextualCommand",
            applicationContextKey = step.ApplicationContextKey,
            applicationContext = context,
            command
        };
    }

    private object ActiveContextualCommand(object command)
    {
        if (!_useApplicationContexts)
            return command;
        if (string.IsNullOrWhiteSpace(_activeApplicationContextKey) || _activeApplicationContext is null)
            return command;
        return new
        {
            type = "contextualCommand",
            applicationContextKey = _activeApplicationContextKey,
            applicationContext = _activeApplicationContext,
            command
        };
    }

    /// <summary>Resolve and select the persisted Web application context on a runtime transition.</summary>
    public async Task ActivateContextWindowAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (!await TryActivateContextWindowAsync(step, cancellationToken))
            throw new InvalidOperationException($"Web application context for Step '{step.Id}' was not resolved.");
    }

    /// <summary>False means that no matching application tab exists yet; ambiguity and transport errors still throw.</summary>
    public async Task<bool> TryActivateContextWindowAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (ApplicationContextFor(step) is null)
            throw new InvalidOperationException($"Web Step '{step.Id}' requires a persisted Web application context for activation.");
        var response = await SendCommandAsync(
            ContextualCommand(step, new { type = "activateContextWindow" }), cancellationToken);
        var status = response.GetProperty("result").GetProperty("status").GetString();
        if (status == "activated") return true;
        if (status == "notFound") return false;
        throw new InvalidOperationException($"Unexpected Web application context activation status '{status}' for Step '{step.Id}'.");
    }

    public Task ArmValidationAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Match the proven Web learner semantics: arming validation must not
        // require the target to exist yet. The target may appear only after a
        // server refresh/document replacement. The live DOM listener is bound
        // when EnsureBubbleShownAsync resolves the current target.
        var armId = Guid.NewGuid().ToString("N");
        lock (_validationGate)
        {
            _commits.Remove(step.Id);
            _armedValidationIds[step.Id] = armId;
        }

        return Task.CompletedTask;
    }

    public async Task<WebValidationCommit?> WaitForValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        await ArmValidationAsync(step, cancellationToken);
        return await WaitForArmedValidationCommitAsync(step, cancellationToken);
    }

    public async Task<WebValidationCommit?> WaitForArmedValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        while (true)
        {
                lock (_validationGate)
            {
                if (_commits.TryGetValue(step.Id, out var queue) && queue.Count > 0)
                    return queue.Peek();
            }

            // Observe cancellation explicitly. Task.WhenAny by itself returns a
            // canceled child task without throwing, which previously left this loop
            // spinning forever after a timed probe cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(
                _signal.WaitAsync(cancellationToken),
                Task.Delay(50, cancellationToken));
            await completed;
        }
    }

    public Task ConsumeValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        lock (_validationGate)
        {
            if (_commits.TryGetValue(step.Id, out var queue) && queue.Count > 0)
                queue.Dequeue();
        }
        return Task.CompletedTask;
    }

    private void ProcessAdapterEvent(JsonElement payload)
    {
        if (!payload.TryGetProperty("type", out var typeElement)) return;
        var eventType = typeElement.GetString();

        if (eventType == "activate-target-window")
        {
            var contextKey = payload.TryGetProperty("applicationContextKey", out var key) ? key.GetString() : null;
            var title = payload.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(title) && string.Equals(contextKey, _activeApplicationContextKey, StringComparison.Ordinal))
                TargetWindowActivationRequested?.Invoke(title);
            return;
        }

        if (eventType == "target-tab-closed")
        {
            var contextKey = payload.TryGetProperty("applicationContextKey", out var key) ? key.GetString() : null;
            if (contextKey != null && string.Equals(contextKey, _activeApplicationContextKey, StringComparison.Ordinal))
                TargetTabClosed?.Invoke();
            return;
        }

        if (eventType == "stop-assistance")
        {
            StopRequested?.Invoke();
            return;
        }

        if (eventType == "centered-dismissed")
        {
            _centeredDismissal?.TrySetResult(true);
            return;
        }

        if (eventType != "validation-commit") return;

        var stepId = payload.TryGetProperty("stepId", out var sid) ? sid.GetString() : null;
        if (string.IsNullOrWhiteSpace(stepId)) return;
        var armId = payload.TryGetProperty("armId", out var aid) ? aid.GetString() : null;
        lock (_validationGate)
        {
            if (string.IsNullOrWhiteSpace(armId) ||
                !_armedValidationIds.TryGetValue(stepId, out var expectedArmId) ||
                !string.Equals(armId, expectedArmId, StringComparison.Ordinal))
                return;
        }

        var kind = payload.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
        var browserEvent = payload.TryGetProperty("browserEvent", out var be) ? be.GetString() : null;
        var hasFocus = payload.TryGetProperty("documentHasFocus", out var dhf) && dhf.ValueKind == JsonValueKind.True;
        var targetIsActive = payload.TryGetProperty("targetIsActive", out var tia) && tia.ValueKind == JsonValueKind.True;
        Console.WriteLine($"[GuideMe validation event] step={stepId} kind={kind} browserEvent={browserEvent ?? "unknown"} documentHasFocus={hasFocus} targetIsActive={targetIsActive}");

        lock (_validationGate)
        {
            if (!_commits.TryGetValue(stepId, out var queue))
                _commits[stepId] = queue = new Queue<WebValidationCommit>();
            queue.Enqueue(new WebValidationCommit(stepId, kind));
        }
        _signal.Release();
    }
    public async Task<WebTargetResolution> ResolveTargetAsync(TargetDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new { type = "resolveTarget", target = descriptor, framePath = descriptor.FrameContext?.Path }, cancellationToken);
        var result = response.GetProperty("result");
        var status = result.GetProperty("status").GetString();
        var count = result.TryGetProperty("count", out var n) ? n.GetInt32() : 0;
        return new WebTargetResolution(status switch
        {
            "resolved" => WebTargetResolutionStatus.Resolved,
            "ambiguous" => WebTargetResolutionStatus.Ambiguous,
            _ => WebTargetResolutionStatus.NotFound
        }, count);
    }

    private async Task<JsonElement> SendCommandAsync(object command, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CommandTimeout);
        var commandToken = timeoutCts.Token;
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingResponses.TryAdd(requestId, completion))
            throw new InvalidOperationException("Could not register GuideMe extension request.");

        try
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            json.Converters.Add(new JsonStringEnumConverter());
            var line = JsonSerializer.Serialize(
                new
                {
                    type = "adapterCommand",
                    requestId,
                    sessionId = _sessionId,
                    runtimeInstanceId = _runtimeInstanceId,
                    command
                },
                json);
            Console.Error.WriteLine($"[GuideMe transport] sending request={requestId}");
            try
            {
                await WritePipeLineAsync(line, commandToken);
                Console.Error.WriteLine($"[GuideMe transport] sent request={requestId}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"GuideMe browser extension / Native Host did not connect to the production Runtime within {CommandTimeout.TotalSeconds:0} seconds.");
            }

            try
            {
                var response = await completion.Task.WaitAsync(commandToken);
                var diagnosticResult = response.TryGetProperty("result", out var resultValue) ? resultValue : default;
                var status = diagnosticResult.ValueKind == JsonValueKind.Object && diagnosticResult.TryGetProperty("status", out var statusValue) ? statusValue.ToString() : "-";
                var count = diagnosticResult.ValueKind == JsonValueKind.Object && diagnosticResult.TryGetProperty("count", out var countValue) ? countValue.ToString() : "-";
                var stable = diagnosticResult.ValueKind == JsonValueKind.Object && diagnosticResult.TryGetProperty("stable", out var stableValue) ? stableValue.ToString() : "-";
                var interactable = diagnosticResult.ValueKind == JsonValueKind.Object && diagnosticResult.TryGetProperty("interactable", out var interactableValue) ? interactableValue.ToString() : "-";
                Console.Error.WriteLine($"[GuideMe transport] response request={requestId} status={status} count={count} stable={stable} interactable={interactable}");
                if (response.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
                    throw new InvalidOperationException(
                        response.TryGetProperty("error", out var error)
                            ? error.GetString()
                            : "Extension adapter command failed.");
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[GuideMe transport] response timeout request={requestId}");
                var suffix = _pendingErrors.TryGetValue(requestId, out var lastError)
                    ? $" Last browser-host response: {lastError}"
                    : string.Empty;
                throw new TimeoutException(
                    $"Extension adapter command '{requestId}' timed out after {CommandTimeout.TotalSeconds:0} seconds.{suffix}");
            }
        }
        finally
        {
            _pendingResponses.TryRemove(requestId, out _);
            _pendingErrors.TryRemove(requestId, out _);
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

                Console.Error.WriteLine($"[GuideMe runtime] Web pipe server waiting: {PipeName}");
                await server.WaitForConnectionAsync(cancellationToken);
                Console.Error.WriteLine($"[GuideMe runtime] Web pipe client connected: {PipeName}");

                lock (_pipeGate)
                    _pipes.Add(server);

                _connectedSignal.Release();

                // Keep accepting additional Native Messaging hosts. Chrome,
                // Edge, and multiple browser profiles may all have the GuideMe
                // extension loaded at the same time. The first host that can
                // successfully resolve the active application becomes the
                // selected browser session for this GuideMe Runtime instance.
                var connectedServer = server;
                _ = Task.Run(() => ReadPipeLoopAsync(connectedServer, cancellationToken), CancellationToken.None);
                server = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[GuideMe runtime] Web pipe I/O error: {ex}");
                // A Native Messaging host may restart after an extension reload.
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[GuideMe runtime] Web pipe accept error: {ex}");
                throw;
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
                if (!root.TryGetProperty("type", out var typeElement)) continue;
                var type = typeElement.GetString();

                if (type == "adapterResponse")
                {
                    var requestId = root.TryGetProperty("requestId", out var id) ? id.GetString() : null;
                    if (requestId is null ||
                        !root.TryGetProperty("response", out var response) ||
                        !_pendingResponses.TryGetValue(requestId, out var completion))
                        continue;

                    var responseClone = response.Clone();
                    var ok = responseClone.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();

                    NamedPipeServerStream? selected;
                    lock (_pipeGate) selected = _selectedPipe;

                    if (selected is null)
                    {
                        if (!ok)
                        {
                            if (responseClone.TryGetProperty("error", out var error))
                                _pendingErrors[requestId] = error.GetString() ?? "Extension adapter command failed.";
                            continue;
                        }

                        // Multiple browser profiles may have the GuideMe extension
                        // connected at the same time. A host that merely returns
                        // a semantic inactive/notFound result must not capture the
                        // Runtime. Bind only to the host that actually resolved the
                        // persisted Application Context for this command.
                        var contextMatched = !root.TryGetProperty("contextMatched", out var matchedElement)
                            || matchedElement.ValueKind != JsonValueKind.False;
                        if (!contextMatched)
                            continue;

                        lock (_pipeGate)
                        {
                            if (_selectedPipe is null)
                                _selectedPipe = stream;
                            selected = _selectedPipe;
                        }

                        if (!ReferenceEquals(selected, stream))
                            continue;

                        Console.Error.WriteLine("[GuideMe runtime] selected browser extension host.");
                        completion.TrySetResult(responseClone);
                        continue;
                    }

                    if (ReferenceEquals(selected, stream))
                        completion.TrySetResult(responseClone);

                    continue;
                }

                if (type == "adapterEvent" && root.TryGetProperty("payload", out var payload))
                {
                    NamedPipeServerStream? selected;
                    lock (_pipeGate) selected = _selectedPipe;
                    if (selected is null || ReferenceEquals(selected, stream))
                        ProcessAdapterEvent(payload.Clone());
                }
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
                {
                    _selectedPipe = null;
                    Console.Error.WriteLine("[GuideMe runtime] selected browser extension host disconnected.");
                }
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
            var wroteAny = false;

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
                        wroteAny = true;
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

            if (wroteAny)
                return;
        }
    }
    public async Task<bool> IsContextActiveAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Context is null)
            return true;
        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var response = await SendCommandAsync(
            ContextualCommand(step, new { type = "isContextActive", context = step.Context, framePath = step.Target.FrameContext?.Path }),
            cancellationToken);
        return response.GetProperty("result").GetProperty("active").GetBoolean();
    }
    public async Task<bool> IsStableForPresentationAsync(GuideStep step, TimeSpan quietWindow, CancellationToken cancellationToken = default)
    {
        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var response = await SendCommandAsync(
            ContextualCommand(step, new { type = "waitForDomQuiet", quietMilliseconds = Math.Max(0, (int)quietWindow.TotalMilliseconds), framePath = step.Target.FrameContext?.Path }),
            cancellationToken);
        if (!response.TryGetProperty("result", out var quietResult) ||
            quietResult.ValueKind != JsonValueKind.Object ||
            !quietResult.TryGetProperty("stable", out var stableValue) ||
            stableValue.ValueKind != JsonValueKind.True)
        {
            TracePresentation(step, "dom-quiet", "waiting", response);
            return false;
        }
        TracePresentation(step, "dom-quiet", "stable");

        // DOM quiet alone is insufficient: an application may still have a
        // loading/busy layer above an otherwise stable target. Do not expose
        // the next learner Step until a real pointer hit can reach its target.
        var interactableResponse = await SendCommandAsync(
            ContextualCommand(step, new { type = "isTargetInteractable", target = step.Target, framePath = step.Target.FrameContext?.Path }),
            cancellationToken);
        if (!interactableResponse.TryGetProperty("result", out var interactableResult) ||
            interactableResult.ValueKind != JsonValueKind.Object ||
            !interactableResult.TryGetProperty("interactable", out var interactableValue) ||
            (interactableValue.ValueKind != JsonValueKind.True &&
             interactableValue.ValueKind != JsonValueKind.False))
        {
            TracePresentation(step, "target-interactable", "missing-field", interactableResponse);
            return false;
        }

        var interactable = interactableValue.GetBoolean();
        TracePresentation(step, "target-interactable", interactable ? "ready" : "not-interactable", interactableResponse);
        return interactable;
    }
    public async Task<bool> IsPrimaryValidationSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Validation is null || step.Target is null) return false;
        if (string.Equals(step.Validation.Kind, "clicked", StringComparison.Ordinal))
        {
                lock (_validationGate)
                return _commits.TryGetValue(step.Id, out var q) && q.Count > 0;
        }
        var response = await SendCommandAsync(ContextualCommand(step, new { type = "readTargetValue", target = step.Target, framePath = step.Target.FrameContext?.Path }), cancellationToken);
        var result = response.GetProperty("result");
        if (result.GetProperty("status").GetString() != "resolved") return false;
        var value = result.TryGetProperty("value", out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() ?? "" : "";
        return step.Validation.Kind switch
        {
            "value-not-empty" or "state-value-not-empty" => !string.IsNullOrWhiteSpace(value),
            "value-equals" or "state-value-equals" => step.Validation.ExpectedValue is not null
                && string.Equals(value, step.Validation.ExpectedValue, StringComparison.Ordinal),
            _ => throw new NotSupportedException($"Unsupported Web validation kind '{step.Validation.Kind}'.")
        };
    }
    public async Task<bool> AreCompletionConditionsSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.CompletionConditions is null || step.CompletionConditions.Count == 0) return true;
        foreach (var condition in step.CompletionConditions)
        {
            if (condition.Target.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException($"Web Guide Step '{step.Id}' contains a non-Web completion target.");
            var response = await SendCommandAsync(ContextualCommand(step, new { type = "inspectTarget", target = condition.Target, framePath = condition.Target.FrameContext?.Path }), cancellationToken);
            var result = response.GetProperty("result");
            var resolved = result.GetProperty("status").GetString() == "resolved";
            var kind = condition.Kind.Trim().ToLowerInvariant();
            if (!GuideCompletionPolicy.IsSupportedObservationKind(kind))
                throw new NotSupportedException($"Unsupported Web completion condition kind '{condition.Kind}'.");
            // "Appeared"/"vanished" describe user-visible presence, not DOM
            // membership. Preserve locator-based "exists"/"not-exists".
            var observed = resolved;
            if (kind is "target-appeared" or "target-vanished")
            {
                var visible = resolved && result.TryGetProperty("visible", out var visibleProperty)
                    && visibleProperty.ValueKind == JsonValueKind.True;
                var key = step.Id + ":" + kind;
                var token = resolved && result.TryGetProperty("documentToken", out var tokenProperty)
                    ? tokenProperty.GetString() : null;
                var revision = resolved && result.TryGetProperty("mutationRevision", out var revisionProperty)
                    ? revisionProperty.GetInt64() : 0;
                if (!_completionTransitions.TryGetValue(key, out var state))
                {
                    _completionTransitions[key] = (visible, !visible, token, revision);
                    // A transition cannot be established by the first sample.
                    return false;
                }
                var changed = token is not null &&
                    (state.DocumentToken != token || state.Revision != revision);
                var sawHidden = state.SawHidden || !visible;
                _completionTransitions[key] = (state.InitialVisible, sawHidden,
                    token ?? state.DocumentToken, revision);
                if (kind == "target-appeared")
                {
                    // An initially visible results container may be refreshed
                    // by the server. Its DOM mutation/document replacement is
                    // the observable result of the user's committed action.
                    if (!visible || !(sawHidden || changed)) return false;
                    observed = true;
                }
                else
                {
                    // Do not complete a disappearance just because a frame is
                    // temporarily detached before the user's action.
                    if (visible || (state.InitialVisible == false && !changed))
                        return false;
                    observed = false;
                }
            }
            var enabled = resolved && result.TryGetProperty("enabled", out var enabledProperty) && enabledProperty.GetBoolean();
            string? value = null;
            if (resolved && result.TryGetProperty("value", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.String)
                value = valueProperty.GetString();
            // Preserve the Web adapter's previous handling of non-resolved targets.
            if (!GuideCompletionPolicy.IsSatisfied(condition, observed, ambiguous: false, enabled, value))
                return false;
        }
        return true;
    }
    public async Task<WebBubblePresentation> EnsureBubbleShownAsync(GuideStep step, int stepNumber, int totalSteps, bool visible = true, CancellationToken cancellationToken = default, string? automaticStepLabel = null)
    {
        if (step.Target is null) return new(WebTargetResolutionStatus.NotFound, 0);
        string? armId;
        lock (_validationGate)
            _armedValidationIds.TryGetValue(step.Id, out armId);

        var response = await SendCommandAsync(ContextualCommand(step, new
        {
            type = "ensureBubble",
            step,
            stepNumber,
            totalSteps,
            armId,
            visible,
            progressText = _texts?.Format("Learner.StepProgress", stepNumber, totalSteps) ?? string.Empty,
            dragText = _texts?.Get("Learner.DragBubble") ?? string.Empty,
            automaticStepLabel,
            direction = _texts?.IsRightToLeft == false ? "ltr" : "rtl",
            framePath = step.Target.FrameContext?.Path
        }), cancellationToken);
        var result = response.GetProperty("result");
        var status = result.GetProperty("status").GetString();
        var count = result.TryGetProperty("count", out var n) ? n.GetInt32() : 0;
        if (stepNumber == 1)
            Console.Error.WriteLine($"[GuideMe first bubble diagnostic] step={step.Id}, context={step.ApplicationContextKey}, visible={visible}, status={status}, count={count}.");
        return new(status switch
        {
            "resolved" => WebTargetResolutionStatus.Resolved,
            "ambiguous" => WebTargetResolutionStatus.Ambiguous,
            _ => WebTargetResolutionStatus.NotFound
        }, count);
    }
    public async Task HideBubbleAsync(CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(ActiveContextualCommand(new { type = "hideBubble" }), cancellationToken);
    }
    public async Task WaitForCenteredStepDismissalAsync(
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken = default)
    {
        _centeredDismissal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await SendCommandAsync(ContextualCommand(step, new
            {
                type = "showCenteredStep",
                step,
                stepNumber,
                totalSteps,
                progressText = _texts?.Format("Learner.StepProgress", stepNumber, totalSteps) ?? string.Empty,
                actionText = _texts?.Get("Learner.Confirm") ?? string.Empty,
                dragText = _texts?.Get("Learner.DragBubble") ?? string.Empty
            }), cancellationToken);

            await _centeredDismissal.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _centeredDismissal = null;
        }
    }
    public async Task<string?> CaptureAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Capture is null) return null;
        if (step.Capture.Runtime != TargetRuntime.Web)
            throw new InvalidOperationException("Web adapter can capture only Web runtime values.");
        var response = await SendCommandAsync(ContextualCommand(step, new { type = "capture", capture = step.Capture, framePath = step.Target?.FrameContext?.Path }), cancellationToken);
        var result = response.GetProperty("result");
        if (result.GetProperty("status").GetString() != "resolved" ||
            !result.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var raw = value.GetString();
        if (raw is null || string.IsNullOrEmpty(step.Capture.Pattern)) return raw;
        var match = System.Text.RegularExpressions.Regex.Match(raw, step.Capture.Pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transportCts.Cancel();
        lock (_pipeGate)
        {
            foreach (var pipe in _pipes.ToArray())
                pipe.Dispose();
            _pipes.Clear();
            _selectedPipe = null;
        }
        foreach (var pending in _pendingResponses.Values) pending.TrySetCanceled();
        _centeredDismissal?.TrySetCanceled();
        _signal.Dispose();
        _pipeWriteLock.Dispose();
        _connectedSignal.Dispose();
        _transportCts.Dispose();
    }
}
