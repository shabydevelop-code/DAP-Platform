using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

const string PipeName = "dap-web-runtime-v1";

var repository = new SqliteGuideStepRepository(new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()));
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

using var shutdown = new CancellationTokenSource();
var nativeOutput = new NativeOutputWriter(output, json);
var bridge = new DapPipeBridge(PipeName, nativeOutput, json);
var bridgeTask = bridge.RunAsync(shutdown.Token);

try
{
    while (true)
    {
        var lengthBytes = new byte[4];
        if (!await ReadExactAsync(input, lengthBytes)) break;

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > 4 * 1024 * 1024)
            throw new InvalidDataException($"Invalid native message length {length}.");

        var payload = new byte[length];
        if (!await ReadExactAsync(input, payload)) break;

        NativeRequest? request = JsonSerializer.Deserialize<NativeRequest>(payload, json);
        object response;
        try
        {
            response = request?.Type switch
            {
                "getGuide" when !string.IsNullOrWhiteSpace(request.GuideId) =>
                    new
                    {
                        requestId = request.RequestId,
                        ok = true,
                        guideId = request.GuideId,
                        steps = await repository.GetStepsAsync(request.GuideId)
                    },
                "ping" => new { requestId = request?.RequestId, ok = true, type = "pong" },
                "adapterEvent" => await bridge.ForwardToDapAsync(request!, shutdown.Token),
                "adapterResponse" => await bridge.ForwardToDapAsync(request!, shutdown.Token),
                _ => new { requestId = request?.RequestId, ok = false, error = "Unsupported native request." }
            };
        }
        catch (Exception ex)
        {
            response = new { requestId = request?.RequestId, ok = false, error = ex.Message };
        }

        await nativeOutput.SendAsync(response, shutdown.Token);
    }
}
finally
{
    shutdown.Cancel();
    try { await bridgeTask; } catch (OperationCanceledException) { }
}

static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset));
        if (read == 0) return false;
        offset += read;
    }
    return true;
}

sealed class NativeOutputWriter
{
    private readonly Stream _output;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NativeOutputWriter(Stream output, JsonSerializerOptions json)
    {
        _output = output;
        _json = json;
    }

    public async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, _json);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(prefix, cancellationToken);
            await _output.WriteAsync(bytes, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

sealed class DapPipeBridge
{
    private readonly string _pipeName;
    private readonly NativeOutputWriter _nativeOutput;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;

    public DapPipeBridge(string pipeName, NativeOutputWriter nativeOutput, JsonSerializerOptions json)
    {
        _pipeName = pipeName;
        _nativeOutput = nativeOutput;
        _json = json;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeClientStream? client = null;
            try
            {
                client = new NamedPipeClientStream(
                    ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(cancellationToken);

                lock (_gate) _pipe = client;

                using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    using var document = JsonDocument.Parse(line);
                    await _nativeOutput.SendAsync(document.RootElement.Clone(), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // DAP may start later or restart independently of the extension.
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pipe, client)) _pipe = null;
                }
                client?.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
                await Task.Delay(100, cancellationToken);
        }
    }

    public async Task<object> ForwardToDapAsync(NativeRequest request, CancellationToken cancellationToken)
    {
        NamedPipeClientStream? pipe;
        lock (_gate) pipe = _pipe;

        if (pipe is not { IsConnected: true })
            return new { requestId = request.RequestId, ok = false, error = "DAP Web Runtime is not connected." };

        var line = JsonSerializer.Serialize(new
        {
            type = request.Type,
            request.RequestId,
            request.TabId,
            request.FrameId,
            payload = request.Payload,
            response = request.Response
        }, _json) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate) pipe = _pipe;
            if (pipe is not { IsConnected: true })
                return new { requestId = request.RequestId, ok = false, error = "DAP Web Runtime disconnected." };

            await pipe.WriteAsync(bytes, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }

        return new { requestId = request.RequestId, ok = true };
    }
}

sealed record NativeRequest(
    string? Type,
    string? RequestId,
    string? GuideId,
    int? TabId,
    int? FrameId,
    JsonElement? Payload,
    JsonElement? Response);
