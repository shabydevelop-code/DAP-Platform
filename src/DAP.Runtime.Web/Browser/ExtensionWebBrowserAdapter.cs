using System.Text.Json;
using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.Runtime.Web.Browser;

/// <summary>
/// Production Web adapter boundary for the browser extension.
///
/// This first slice owns only the extension event channel. Browser commands are
/// intentionally not implemented yet: the migration remains incremental and
/// Playwright stays the regression baseline until each adapter primitive has
/// demonstrated parity.
/// </summary>
public sealed class ExtensionWebBrowserAdapter : IWebBrowserAdapter, IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly string _eventPath;
    private readonly string _commandPath;
    private readonly string _responsePath;
    private long _responseOffset;
    private readonly Dictionary<string, Queue<WebValidationCommit>> _commits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _armedValidationIds = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);
    private long _offset;
    private bool _disposed;

    public ExtensionWebBrowserAdapter(string? eventPath = null, string? commandPath = null, string? responsePath = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DAP", "WebAdapter");
        _eventPath = eventPath ?? Path.Combine(directory, "events.jsonl");
        _commandPath = commandPath ?? Path.Combine(directory, "commands.jsonl");
        _responsePath = responsePath ?? Path.Combine(directory, "responses.jsonl");

        // These files are transport journals, not durable history. A new DAP
        // runtime session must not make a newly started NativeHost replay every
        // command left by previous probe/runtime sessions before it reaches the
        // current request.
        Directory.CreateDirectory(directory);
        ResetSharedJournal(_commandPath);
        ResetSharedJournal(_responsePath);
        ResetSharedJournal(_eventPath);
        _offset = 0;
        _responseOffset = 0;
    }

    public async Task ArmValidationAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        ReadPendingEvents();
        _commits.Remove(step.Id);
        var armId = Guid.NewGuid().ToString("N");
        _armedValidationIds[step.Id] = armId;
        var armed = await SendCommandAsync(new { type = "armValidation", step, armId, framePath = step.Target?.FrameContext?.Path }, cancellationToken);
        var result = armed.GetProperty("result");
        if (result.GetProperty("status").GetString() != "resolved")
            throw new InvalidOperationException($"Validation target for Step '{step.Id}' did not resolve exactly once.");
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
            ReadPendingEvents();
            if (_commits.TryGetValue(step.Id, out var queue) && queue.Count > 0)
                return queue.Peek();

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
        ReadPendingEvents();
        if (_commits.TryGetValue(step.Id, out var queue) && queue.Count > 0)
            queue.Dequeue();
        return Task.CompletedTask;
    }

    private void ReadPendingEvents()
    {
        if (!File.Exists(_eventPath)) return;

        using var stream = new FileStream(_eventPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (_offset > stream.Length) _offset = 0;
        stream.Position = _offset;
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            _offset = stream.Position;
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("payload", out var payload)) continue;
            if (!payload.TryGetProperty("type", out var type) || type.GetString() != "validation-commit") continue;
            var stepId = payload.TryGetProperty("stepId", out var sid) ? sid.GetString() : null;
            if (string.IsNullOrWhiteSpace(stepId)) continue;
            var armId = payload.TryGetProperty("armId", out var aid) ? aid.GetString() : null;
            if (string.IsNullOrWhiteSpace(armId) || !_armedValidationIds.TryGetValue(stepId, out var expectedArmId) || !string.Equals(armId, expectedArmId, StringComparison.Ordinal)) continue;
            var kind = payload.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            var browserEvent = payload.TryGetProperty("browserEvent", out var be) ? be.GetString() : null;
            var hasFocus = payload.TryGetProperty("documentHasFocus", out var dhf) && dhf.ValueKind == JsonValueKind.True;
            var targetIsActive = payload.TryGetProperty("targetIsActive", out var tia) && tia.ValueKind == JsonValueKind.True;
            Console.WriteLine($"[DAP validation event] step={stepId} kind={kind} browserEvent={browserEvent ?? "unknown"} documentHasFocus={hasFocus} targetIsActive={targetIsActive}");
            if (!_commits.TryGetValue(stepId, out var queue))
                _commits[stepId] = queue = new Queue<WebValidationCommit>();
            queue.Enqueue(new WebValidationCommit(stepId, kind));
            _signal.Release();
        }
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
        Directory.CreateDirectory(Path.GetDirectoryName(_commandPath)!);
        // No frameId here. Until a TargetDescriptor FrameContext is mapped to a
        // concrete browser frame, the extension must query all injected frames.
        // Sending frameId=0 incorrectly forces the command into the top frame.
        var line = JsonSerializer.Serialize(
            new { type = "adapterCommand", requestId, command },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await AppendSharedLineAsync(_commandPath, line, commandToken);

        while (true)
        {
            if (commandToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Extension adapter command '{requestId}' timed out after {CommandTimeout.TotalSeconds:0} seconds.");
            }
            if (File.Exists(_responsePath))
            {
                using var stream = new FileStream(_responsePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (_responseOffset > stream.Length) _responseOffset = 0;
                stream.Position = _responseOffset;
                using var reader = new StreamReader(stream);
                string? responseLine;
                while ((responseLine = reader.ReadLine()) is not null)
                {
                    _responseOffset = stream.Position;
                    if (string.IsNullOrWhiteSpace(responseLine)) continue;
                    using var doc = JsonDocument.Parse(responseLine);
                    if (!doc.RootElement.TryGetProperty("requestId", out var id) || id.GetString() != requestId) continue;
                    var response = doc.RootElement.GetProperty("response").Clone();
                    if (response.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
                        throw new InvalidOperationException(response.TryGetProperty("error", out var error) ? error.GetString() : "Extension adapter command failed.");
                    return response;
                }
            }
            await Task.Delay(25, commandToken);
        }
    }

    private static void ResetSharedJournal(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Flush();
    }

    private static async Task AppendSharedLineAsync(string path, string line, CancellationToken cancellationToken)
    {
        // NativeHost tails this journal concurrently. Open explicitly with
        // FileShare.ReadWrite instead of File.AppendAllTextAsync, whose sharing
        // mode can collide with the reader during normal runtime traffic.
        var bytes = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096, useAsync: true);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    private static NotSupportedException Pending(string operation)
        => new($"Extension Web adapter operation '{operation}' has not been migrated yet.");

    public async Task<bool> IsContextActiveAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Context is null)
            return true;
        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var response = await SendCommandAsync(
            new { type = "isContextActive", context = step.Context, framePath = step.Target.FrameContext?.Path },
            cancellationToken);
        return response.GetProperty("result").GetProperty("active").GetBoolean();
    }
    public async Task<bool> IsStableForPresentationAsync(GuideStep step, TimeSpan quietWindow, CancellationToken cancellationToken = default)
    {
        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var response = await SendCommandAsync(
            new { type = "waitForDomQuiet", quietMilliseconds = Math.Max(0, (int)quietWindow.TotalMilliseconds), framePath = step.Target.FrameContext?.Path },
            cancellationToken);
        return response.GetProperty("result").GetProperty("stable").GetBoolean();
    }
    public async Task<bool> IsPrimaryValidationSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Validation is null || step.Target is null) return false;
        if (string.Equals(step.Validation.Kind, "clicked", StringComparison.Ordinal))
        {
            ReadPendingEvents();
            return _commits.TryGetValue(step.Id, out var q) && q.Count > 0;
        }
        var response = await SendCommandAsync(new { type = "readTargetValue", target = step.Target, framePath = step.Target.FrameContext?.Path }, cancellationToken);
        var result = response.GetProperty("result");
        if (result.GetProperty("status").GetString() != "resolved") return false;
        var value = result.TryGetProperty("value", out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() ?? "" : "";
        return step.Validation.Kind switch
        {
            "value-not-empty" => !string.IsNullOrWhiteSpace(value),
            "value-equals" => step.Validation.ExpectedValue is not null && string.Equals(value, step.Validation.ExpectedValue, StringComparison.Ordinal),
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
            var response = await SendCommandAsync(new { type = "inspectTarget", target = condition.Target, framePath = condition.Target.FrameContext?.Path }, cancellationToken);
            var result = response.GetProperty("result");
            var resolved = result.GetProperty("status").GetString() == "resolved";
            switch (condition.Kind.Trim().ToLowerInvariant())
            {
                case "target-exists": if (!resolved) return false; break;
                case "target-not-exists": if (resolved) return false; break;
                case "target-enabled":
                    if (!resolved || !result.TryGetProperty("enabled", out var enabled) || !enabled.GetBoolean()) return false;
                    break;
                case "value-equals":
                    if (!resolved || condition.ExpectedValue is null ||
                        !result.TryGetProperty("value", out var value) ||
                        !string.Equals(value.GetString(), condition.ExpectedValue, StringComparison.Ordinal)) return false;
                    break;
                default: throw new NotSupportedException($"Unsupported Web completion condition kind '{condition.Kind}'.");
            }
        }
        return true;
    }
    public async Task<WebBubblePresentation> EnsureBubbleShownAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default)
    {
        if (step.Target is null) return new(WebTargetResolutionStatus.NotFound, 0);
        var response = await SendCommandAsync(new { type = "ensureBubble", step, stepNumber, totalSteps, framePath = step.Target.FrameContext?.Path }, cancellationToken);
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
        await SendCommandAsync(new { type = "hideBubble" }, cancellationToken);
    }
    public Task WaitForCenteredStepDismissalAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default) => throw Pending(nameof(WaitForCenteredStepDismissalAsync));
    public Task WaitForGuideCompletedDismissalAsync(CancellationToken cancellationToken = default) => throw Pending(nameof(WaitForGuideCompletedDismissalAsync));
    public async Task<string?> CaptureAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Capture is null) return null;
        if (step.Capture.Runtime != TargetRuntime.Web)
            throw new InvalidOperationException("Web adapter can capture only Web runtime values.");
        var response = await SendCommandAsync(new { type = "capture", capture = step.Capture, framePath = step.Target?.FrameContext?.Path }, cancellationToken);
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
        _signal.Dispose();
    }
}
