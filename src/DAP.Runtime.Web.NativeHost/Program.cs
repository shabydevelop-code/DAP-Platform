using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

var repository = new SqliteGuideStepRepository(new SqliteConnectionFactory(SqliteDatabaseOptions.CreateDefault()));
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

while (true)
{
    var lengthBytes = new byte[4];
    if (!await ReadExactAsync(input, lengthBytes)) break;
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

sealed record NativeRequest(string? Type, string? RequestId, string? GuideId);
