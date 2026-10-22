using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

const int maxMessage = 1024 * 1024;
const string pipePrefix = "guideme-instructor-v1-";
var pipeName = pipePrefix + System.Diagnostics.Process.GetCurrentProcess().SessionId;
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
while (true)
{
    var header = new byte[4];
    if (!await ReadExact(input, header)) break;
    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length <= 0 || length > maxMessage) break;
    var bytes = new byte[length];
    if (!await ReadExact(input, bytes)) break;
    object reply;
    string? requestId = null;
    try
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String)
            requestId = id.GetString();
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(Encoding.UTF8.GetString(bytes).AsMemory(), timeout.Token);
        var response = await reader.ReadLineAsync(timeout.Token);
        if (response is null) throw new IOException("Instructor pipe closed.");
        using var answer = JsonDocument.Parse(response);
        reply = answer.RootElement.Clone();
    }
    catch (Exception ex)
    {
        reply = new { requestId, ok = false, error = ex.Message };
    }
    var data = JsonSerializer.SerializeToUtf8Bytes(reply);
    BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
    await output.WriteAsync(header);
    await output.WriteAsync(data);
    await output.FlushAsync();
}

static async Task<bool> ReadExact(Stream stream, byte[] bytes)
{
    var count = 0;
    while (count < bytes.Length)
    {
        var read = await stream.ReadAsync(bytes.AsMemory(count));
        if (read == 0) return false;
        count += read;
    }
    return true;
}
