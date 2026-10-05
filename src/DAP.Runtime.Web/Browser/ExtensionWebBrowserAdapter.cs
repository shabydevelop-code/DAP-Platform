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
    private readonly SemaphoreSlim _signal = new(0);
    private long _offset;
    private bool _disposed;

    public ExtensionWebBrowserAdapter(string? eventPath = null, string? commandPath = null, string? responsePath = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DAP", "WebAdapter");
        _eventPath = eventPath ?? Path.Combine(directory, "events.jsonl");
        _commandPath = commandPath ?? Path.Combine(directory, "commands.jsonl");
        _responsePath = responsePath ?? Path.Combine(directory, "responses.jsonl");
        if (File.Exists(_eventPath)) _offset = new FileInfo(_eventPath).Length;
        if (File.Exists(_responsePath)) _responseOffset = new FileInfo(_responsePath).Length;
    }

    public async Task<WebValidationCommit?> WaitForValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        var armed = await SendCommandAsync(new { type = "armValidation", step }, cancellationToken);
        var armedResult = armed.GetProperty("result");
        if (armedResult.GetProperty("status").GetString() != "resolved")
            return null;

        while (true)
        {
            ReadPendingEvents();
            if (_commits.TryGetValue(step.Id, out var queue) && queue.Count > 0)
                return queue.Peek();

            await Task.WhenAny(
                _signal.WaitAsync(cancellationToken),
                Task.Delay(50, cancellationToken));
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
            var kind = payload.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            if (!_commits.TryGetValue(stepId, out var queue))
                _commits[stepId] = queue = new Queue<WebValidationCommit>();
            queue.Enqueue(new WebValidationCommit(stepId, kind));
            _signal.Release();
        }
    }

    public async Task<WebTargetResolution> ResolveTargetAsync(TargetDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new { type = "resolveTarget", target = descriptor }, cancellationToken);
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
        await File.AppendAllTextAsync(_commandPath, line + Environment.NewLine, commandToken);

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

    private static NotSupportedException Pending(string operation)
        => new($"Extension Web adapter operation '{operation}' has not been migrated yet.");

    public async Task<bool> IsContextActiveAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
        if (step.Context is null)
            return true;
        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var response = await SendCommandAsync(
            new { type = "isContextActive", context = step.Context },
            cancellationToken);
        return response.GetProperty("result").GetProperty("active").GetBoolean();
    }
    public async Task<bool> IsStableForPresentationAsync(GuideStep step, TimeSpan quietWindow, CancellationToken cancellationToken = default)
    {
        if (step.Target?.Runtime != TargetRuntime.Web)
            return false;

        var response = await SendCommandAsync(
            new { type = "waitForDomQuiet", quietMilliseconds = Math.Max(0, (int)quietWindow.TotalMilliseconds) },
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
        var response = await SendCommandAsync(new { type = "readTargetValue", target = step.Target }, cancellationToken);
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
    public Task<bool> AreCompletionConditionsSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default) => throw Pending(nameof(AreCompletionConditionsSatisfiedAsync));
    public Task<WebBubblePresentation> EnsureBubbleShownAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default) => throw Pending(nameof(EnsureBubbleShownAsync));
    public Task HideBubbleAsync(CancellationToken cancellationToken = default) => throw Pending(nameof(HideBubbleAsync));
    public Task WaitForCenteredStepDismissalAsync(GuideStep step, int stepNumber, int totalSteps, CancellationToken cancellationToken = default) => throw Pending(nameof(WaitForCenteredStepDismissalAsync));
    public Task WaitForGuideCompletedDismissalAsync(CancellationToken cancellationToken = default) => throw Pending(nameof(WaitForGuideCompletedDismissalAsync));
    public Task<string?> CaptureAsync(GuideStep step, CancellationToken cancellationToken = default) => throw Pending(nameof(CaptureAsync));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signal.Dispose();
    }
}
