using Microsoft.Playwright;
using System.Collections.Concurrent;

namespace DAP.Runtime.Web.Validation;

/// <summary>
/// Keeps event-based Web validation state inside DAP.exe so completion survives
/// target, frame and document replacement in the guided application.
/// </summary>
public sealed class WebValidationSession : IAsyncDisposable
{
    public const string BrowserBindingName = "__dapReportValidation";

    private readonly ConcurrentDictionary<string, byte> _completedSteps = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _bridgeLock = new(1, 1);
    private IPage? _boundPage;
    private readonly ConcurrentQueue<string> _diagnostics = new();

    public async Task EnsureBridgeAsync(IPage page)
    {
        if (ReferenceEquals(_boundPage, page))
            return;

        await _bridgeLock.WaitAsync();
        try
        {
            if (ReferenceEquals(_boundPage, page))
                return;

            await page.ExposeBindingAsync<string>(
                BrowserBindingName,
                (_, stepId) =>
                {
                    if (!string.IsNullOrWhiteSpace(stepId))
                    {
                        _completedSteps[stepId] = 0;
                        Trace($"[DAP validation] completion event received for Step '{stepId}'.");
                    }
                });

            _boundPage = page;
        }
        finally
        {
            _bridgeLock.Release();
        }
    }

    public bool IsCompleted(string stepId)
        => _completedSteps.ContainsKey(stepId);

    public void Trace(string message)
    {
        Console.Error.WriteLine(message);
        _diagnostics.Enqueue(message);
        while (_diagnostics.Count > 200)
            _diagnostics.TryDequeue(out _);
    }

    public IReadOnlyList<string> GetDiagnostics()
        => _diagnostics.ToArray();

    public ValueTask DisposeAsync()
    {
        _boundPage = null;
        _completedSteps.Clear();
        _bridgeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
