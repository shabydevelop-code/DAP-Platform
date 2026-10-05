using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

var repository = new SqliteGuideStepRepository(new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()));
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

var adapterDirectory = Path.Combine(Path.GetTempPath(), "DAP", "WebAdapter");
Directory.CreateDirectory(adapterDirectory);
var commandPath = Path.Combine(adapterDirectory, "commands.jsonl");
// Read the journal from the beginning. Commands carry unique request IDs, and
// starting at EOF can drop a command written just before Chrome starts/restarts
// the native host.
long commandOffset = 0;

while (true)
{
    var lengthBytes = new byte[4];
    var headerRead = ReadExactAsync(input, lengthBytes);
    while (!headerRead.IsCompleted)
    {
        commandOffset = await ForwardPendingCommandsAsync(commandPath, commandOffset, output, json);
        await Task.WhenAny(headerRead, Task.Delay(25));
    }
    if (!await headerRead) break;
    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
    if (length <= 0 || length > 4 * 1024 * 1024) throw new InvalidDataException($"Invalid native message length {length}.");
    var payload = new byte[length];
    if (!await ReadExactAsync(input, payload)) break;

    NativeRequest? request = JsonSerializer.Deserialize<NativeRequest>(payload, json);
    object response;
    try
    {
        response = request?.Type switch
        {
            "getGuide" when !string.IsNullOrWhiteSpace(request.GuideId) =>
                new { requestId = request.RequestId, ok = true, guideId = request.GuideId,
                    steps = await repository.GetStepsAsync(request.GuideId) },
            "ping" => new { requestId = request?.RequestId, ok = true, type = "pong" },
            "adapterEvent" => await ForwardAdapterEventAsync(request!, json),
            "adapterResponse" => await ForwardAdapterResponseAsync(request!, json),
            _ => new { requestId = request?.RequestId, ok = false, error = "Unsupported native request." }
        };
    }
    catch (Exception ex)
    {
        response = new { requestId = request?.RequestId, ok = false, error = ex.Message };
    }

    var bytes = JsonSerializer.SerializeToUtf8Bytes(response, json);
    var prefix = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
    await output.WriteAsync(prefix);
    await output.WriteAsync(bytes);
    await output.FlushAsync();
}

static async Task<long> ForwardPendingCommandsAsync(string path, long offset, Stream output, JsonSerializerOptions json)
{
    if (!File.Exists(path)) return offset;
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    if (offset > stream.Length) offset = 0;
    stream.Position = offset;
    using var reader = new StreamReader(stream);
    string? line;
    while ((line = await reader.ReadLineAsync()) is not null)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        using var doc = JsonDocument.Parse(line);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc.RootElement, json);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await output.WriteAsync(prefix);
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    }
    // StreamReader buffers ahead, so stream.Position is not a reliable
    // per-line cursor. At EOF it is, however, the exact byte position to resume
    // from on the next polling pass.
    return stream.Position;
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

static async Task<object> ForwardAdapterEventAsync(NativeRequest request, JsonSerializerOptions json)
{
    // Native Messaging starts one host process per extension connection. Until
    // DAP owns this process directly, persist adapter events in a local IPC
    // journal so the .NET adapter can consume browser facts without moving
    // guide policy into the extension.
    var directory = Path.Combine(Path.GetTempPath(), "DAP", "WebAdapter");
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "events.jsonl");
    var line = JsonSerializer.Serialize(new
    {
        utc = DateTimeOffset.UtcNow,
        request.TabId,
        request.FrameId,
        payload = request.Payload
    }, json);
    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
    return new { requestId = request.RequestId, ok = true };
}

static async Task<object> ForwardAdapterResponseAsync(NativeRequest request, JsonSerializerOptions json)
{
    var directory = Path.Combine(Path.GetTempPath(), "DAP", "WebAdapter");
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "responses.jsonl");
    var line = JsonSerializer.Serialize(new
    {
        utc = DateTimeOffset.UtcNow,
        request.RequestId,
        request.TabId,
        request.FrameId,
        response = request.Response
    }, json);
    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
    return new { requestId = request.RequestId, ok = true };
}

sealed record NativeRequest(
    string? Type,
    string? RequestId,
    string? GuideId,
    int? TabId,
    int? FrameId,
    JsonElement? Payload,
    JsonElement? Response);
