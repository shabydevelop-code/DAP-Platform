using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DAP.TestCRM.Web.E2E;

internal sealed class ExtensionTestDriver : IAsyncDisposable
{
    private const string PipeName = "dap-web-e2e-v1";
    private readonly NamedPipeServerStream _pipe = new(
        PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _sessionId;

    public ExtensionTestDriver(string sessionId)
    {
        _sessionId = sessionId;
        _reader = new StreamReader(_pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
        => await _pipe.WaitForConnectionAsync(cancellationToken);

    public async Task<JsonElement> SendAsync(object command, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var line = JsonSerializer.Serialize(new
        {
            type = "testDriverCommand",
            requestId,
            sessionId = _sessionId,
            command
        }) + "\n";

        var bytes = Encoding.UTF8.GetBytes(line);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _pipe.WriteAsync(bytes, cancellationToken);
            await _pipe.FlushAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }

        while (true)
        {
            var responseLine = await _reader.ReadLineAsync(cancellationToken)
                ?? throw new IOException("DAP Extension test-driver pipe disconnected.");
            using var document = JsonDocument.Parse(responseLine);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) &&
                type.GetString() == "testDriverResponse" &&
                root.TryGetProperty("requestId", out var id) &&
                id.GetString() == requestId)
            {
                var response = root.GetProperty("response").Clone();
                if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                    throw new InvalidOperationException(
                        response.TryGetProperty("error", out var error)
                            ? error.GetString()
                            : "DAP Extension test-driver command failed.");
                return response;
            }
        }
    }

    public Task<JsonElement> LocatorAsync(string frameName, string selector, string op, string? value = null, CancellationToken cancellationToken = default)
        => SendAsync(new
        {
            type = "testLocator",
            frameName,
            path = new[] { new { selector, index = 0 } },
            op,
            value
        }, cancellationToken);

    public Task<JsonElement> KeyboardAsync(string frameName, string key, CancellationToken cancellationToken = default)
        => SendAsync(new { type = "testKeyboard", frameName, key }, cancellationToken);

    public Task<JsonElement> TypeAsync(string frameName, string value, CancellationToken cancellationToken = default)
        => SendAsync(new { type = "testType", frameName, value }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _pipe.DisposeAsync();
        _writeGate.Dispose();
    }
}
