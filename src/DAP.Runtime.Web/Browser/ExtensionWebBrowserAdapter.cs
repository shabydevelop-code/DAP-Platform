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
    private readonly string _eventPath;
    private readonly Dictionary<string, Queue<WebValidationCommit>> _commits = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);
    private long _offset;
    private bool _disposed;

    public ExtensionWebBrowserAdapter(string? eventPath = null)
    {
        _eventPath = eventPath ?? Path.Combine(Path.GetTempPath(), "DAP", "WebAdapter", "events.jsonl");
        if (File.Exists(_eventPath))
            _offset = new FileInfo(_eventPath).Length;
    }

    public async Task<WebValidationCommit?> WaitForValidationCommitAsync(GuideStep step, CancellationToken cancellationToken = default)
    {
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

    private static NotSupportedException Pending(string operation)
        => new($"Extension Web adapter operation '{operation}' has not been migrated yet.");

    public Task<WebTargetResolution> ResolveTargetAsync(TargetDescriptor descriptor, CancellationToken cancellationToken = default) => throw Pending(nameof(ResolveTargetAsync));
    public Task<bool> IsContextActiveAsync(GuideStep step, CancellationToken cancellationToken = default) => throw Pending(nameof(IsContextActiveAsync));
    public Task<bool> IsStableForPresentationAsync(GuideStep step, TimeSpan quietWindow, CancellationToken cancellationToken = default) => throw Pending(nameof(IsStableForPresentationAsync));
    public Task<bool> IsPrimaryValidationSatisfiedAsync(GuideStep step, CancellationToken cancellationToken = default) => throw Pending(nameof(IsPrimaryValidationSatisfiedAsync));
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
