using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Core.Guides;
using DAP.Core.Localization;
using DAP.Core.Targets;

namespace DAP.Runtime.Web.Browser;

/// <summary>
/// Production Web adapter boundary for the browser extension.
/// Guide behavior remains owned by DAP Runtime; browser access is routed through
/// Native Messaging hosts connected to the DAP named-pipe transport.
/// </summary>
public sealed class ExtensionWebBrowserAdapter : IWebBrowserAdapter, IDisposable
{
    private static readonly string PipeName = "dap-web-runtime-v1-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingResponses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pendingErrors = new(StringComparer.Ordinal);
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
        _sessionId = Environment.GetEnvironmentVariable("DAP_WEB_SESSION_ID");
        _acceptLoop = Task.Run(() => AcceptPipeLoopAsync(_transportCts.Token));
        _ = _acceptLoop.ContinueWith(
            task => Console.Error.WriteLine(
                $"[DAP runtime] Web pipe accept loop faulted: {task.Exception?.GetBaseException()}"),
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
        Console.WriteLine($"[DAP validation event] step={stepId} kind={kind} browserEvent={browserEvent ?? "unknown"} documentHasFocus={hasFocus} targetIsActive={targetIsActive}");

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
            throw new InvalidOperationException("Could not register DAP extension request.");

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
            Console.Error.WriteLine($"[DAP transport] sending request={requestId}");
            try
            {
                await WritePipeLineAsync(line, commandToken);
                Console.Error.WriteLine($"[DAP transport] sent request={requestId}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"DAP browser extension / Native Host did not connect to the production Runtime within {CommandTimeout.TotalSeconds:0} seconds.");
            }

            try
            {
                var response = await completion.Task.WaitAsync(commandToken);
                Console.Error.WriteLine($"[DAP transport] received response request={requestId}");
                if (response.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
                    throw new InvalidOperationException(
                        response.TryGetProperty("error", out var error)
                            ? error.GetString()
                            : "Extension adapter command failed.");
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[DAP transport] response timeout request={requestId}");
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

                Console.Error.WriteLine($"[DAP runtime] Web pipe server waiting: {PipeName}");
                await server.WaitForConnectionAsync(cancellationToken);
                Console.Error.WriteLine($"[DAP runtime] Web pipe client connected: {PipeName}");

                lock (_pipeGate)
                    _pipes.Add(server);

                _connectedSignal.Release();

                // Keep accepting additional Native Messaging hosts. Chrome,
                // Edge, and multiple browser profiles may all have the DAP
                // extension loaded at the same time. The first host that can
                // successfully resolve the active application becomes the
                // selected browser session for this DAP Runtime instance.
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
                Console.Error.WriteLine($"[DAP runtime] Web pipe I/O error: {ex}");
                // A Native Messaging host may restart after an extension reload.
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[DAP runtime] Web pipe accept error: {ex}");
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

                        // Multiple browser profiles may have the DAP extension
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

                        Console.Error.WriteLine("[DAP runtime] selected browser extension host.");
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
                    Console.Error.WriteLine("[DAP runtime] selected browser extension host disconnected.");
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
        if (!response.GetProperty("result").GetProperty("stable").GetBoolean())
            return false;

        // DOM quiet alone is insufficient: an application may still have a
        // loading/busy layer above an otherwise stable target. Do not expose
        // the next learner Step until a real pointer hit can reach its target.
        var interactableResponse = await SendCommandAsync(
            ContextualCommand(step, new { type = "isTargetInteractable", target = step.Target, framePath = step.Target.FrameContext?.Path }),
            cancellationToken);
        return interactableResponse.GetProperty("result").GetProperty("interactable").GetBoolean();
    }
    public async Task ApplyAutomationValueAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Target is null || string.IsNullOrEmpty(step.AutomationValue))
            throw new InvalidOperationException($"Web Hybrid Step '{step.Id}' has no automation target/value.");
        if (step.AdvanceMode != StepAdvanceMode.AutomaticOnValidation ||
            step.Validation is null || !GuideValidationPolicy.IsValueValidation(step.Validation))
            throw new InvalidOperationException($"Web Hybrid Step '{step.Id}' requires automatic value validation.");
        var response = await SendCommandAsync(ContextualCommand(step, new
        {
            type = "applyAutomationValue", target = step.Target,
            framePath = step.Target.FrameContext?.Path, value = step.AutomationValue
        }), cancellationToken);
        if (response.GetProperty("result").GetProperty("status").GetString() != "applied")
            throw new InvalidOperationException($"Web Hybrid Step '{step.Id}' could not resolve its unique writable target.");
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
            "value-not-empty" => GuideValidationPolicy.IsValueSatisfied(step.Validation, value),
            "value-equals" => step.Validation.ExpectedValue is not null && GuideValidationPolicy.IsValueSatisfied(step.Validation, value),
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
            var enabled = resolved && result.TryGetProperty("enabled", out var enabledProperty) && enabledProperty.GetBoolean();
            string? value = null;
            if (resolved && result.TryGetProperty("value", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.String)
                value = valueProperty.GetString();
            // Preserve the Web adapter's previous handling of non-resolved targets.
            if (!GuideCompletionPolicy.IsSatisfied(condition, resolved, ambiguous: false, enabled, value))
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
            progressText = _texts?.Format("Learner.StepProgress", stepNumber, totalSteps) ?? $"שלב {stepNumber} מתוך {totalSteps}",
            dragText = _texts?.Get("Learner.DragBubble") ?? "גרור להזזת הבועה",
            stopText = _texts?.Get("Learner.StopAssistance") ?? "סיים ליווי",
            automaticStepLabel,
            direction = _texts?.IsRightToLeft == false ? "ltr" : "rtl",
            framePath = step.Target.FrameContext?.Path
        }), cancellationToken);
        var result = response.GetProperty("result");
        var status = result.GetProperty("status").GetString();
        var count = result.TryGetProperty("count", out var n) ? n.GetInt32() : 0;
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
                progressText = _texts?.Format("Learner.StepProgress", stepNumber, totalSteps) ?? $"שלב {stepNumber} מתוך {totalSteps}",
                actionText = _texts?.Get("Learner.Confirm") ?? "אישור",
                dragText = _texts?.Get("Learner.DragBubble") ?? "גרור להזזת הבועה"
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
