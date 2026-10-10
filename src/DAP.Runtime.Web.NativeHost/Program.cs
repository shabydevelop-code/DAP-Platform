using System.Diagnostics;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

var runtimePipeName = "dap-web-runtime-v1-" + Process.GetCurrentProcess().SessionId;

using var nativeLog = new NativeHostLog();
nativeLog.Write("process-start", $"pid={Environment.ProcessId}; args=[{string.Join(", ", args)}]");

var repository = new SqliteGuideStepRepository(new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()));
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

using var shutdown = new CancellationTokenSource();
var nativeOutput = new NativeOutputWriter(output, json);
var runtimeBridge = new DapPipeBridge(runtimePipeName, nativeOutput, json, nativeLog);
var runtimeBridgeTask = runtimeBridge.RunAsync(shutdown.Token);

try
{
    nativeLog.Write("native-loop", "waiting for browser messages");
    while (true)
    {
        var lengthBytes = new byte[4];
        if (!await ReadExactAsync(input, lengthBytes))
        {
            nativeLog.Write("native-stdin-closed", "browser closed Native Messaging stdin");
            break;
        }

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
                "adapterEvent" => await runtimeBridge.ForwardToDapAsync(request!, shutdown.Token),
                "adapterResponse" => await runtimeBridge.ForwardToDapAsync(request!, shutdown.Token),
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
    try { await runtimeBridgeTask; } catch (OperationCanceledException) { }
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
    private readonly NativeHostLog _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;

    public DapPipeBridge(string pipeName, NativeOutputWriter nativeOutput, JsonSerializerOptions json, NativeHostLog log)
    {
        _pipeName = pipeName;
        _nativeOutput = nativeOutput;
        _json = json;
        _log = log;
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
                _log.Write("pipe-connect-attempt", _pipeName);
                await client.ConnectAsync(cancellationToken);

                lock (_gate) _pipe = client;
                _log.Write("pipe-connected", _pipeName);

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
            catch (IOException ex)
            {
                _log.Write("pipe-io", _pipeName + ": " + ex.Message);
                // DAP may start later or restart independently of the extension.
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _log.Write("pipe-error", _pipeName + ": " + ex);
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
            request.SessionId,
            command = request.Command,
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
    string? SessionId,
    JsonElement? Command,
    JsonElement? Payload,
    JsonElement? Response);


sealed class NativeHostLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public NativeHostLog()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DAP",
            "Logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "native-host.log");
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public void Write(string kind, string message)
    {
        lock (_gate)
        {
            _writer.WriteLine(
                $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {kind}: {message}");
        }
    }

    public void Dispose() => _writer.Dispose();
}
