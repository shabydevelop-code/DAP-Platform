using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace GuideMe.Instructor.Web;

// The browser extension only communicates through its dedicated Chrome native host.
// The pipe is restricted to the current Windows user; the endpoint is not exposed on HTTP.
internal sealed class InstructorNativePipe
{
    private readonly InstructorCaptureBridge _capture;
    private readonly string _pipeName = "guideme-instructor-v1-" +
        System.Diagnostics.Process.GetCurrentProcess().SessionId;
    internal InstructorNativePipe(InstructorCaptureBridge capture) => _capture = capture;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                await HandleAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { Console.Error.WriteLine("Instructor native pipe: " + ex.Message); }
            finally { await server.DisposeAsync(); }
        }
    }

    private async Task HandleAsync(Stream stream, CancellationToken shutdown)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(timeout.Token);
        if (line is null || line.Length > 1024 * 1024) return;
        object response;
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            requestId = root.GetProperty("requestId").GetString();
            var type = root.GetProperty("type").GetString();
            object? result = type switch
            {
                "heartbeat" => _capture.Heartbeat(root.GetProperty("tabs").Deserialize<CaptureTab[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                    Array.Empty<CaptureTab>()),
                "event" => ReceiveEvent(root.GetProperty("payload").Deserialize<CaptureEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                "picker-armed" => _capture.PickerArmed(root.GetProperty("tabId").GetInt32()),
                "picker-error" => _capture.PickerFailed(root.GetProperty("tabId").GetInt32(), root.GetProperty("reason").GetString() ?? "Unknown picker error"),
                "picker-cancelled" => _capture.PickerCancelled(root.GetProperty("tabId").GetInt32()),
                "picked" => _capture.Picked(root.GetProperty("payload").Deserialize<CaptureEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new ArgumentException("Missing picked element.")),
                _ => throw new InvalidOperationException("Unknown Instructor native request.")
            };
            response = new { requestId, ok = true, result };
        }
        catch (Exception ex) { response = new { requestId, ok = false, error = ex.Message }; }
        await writer.WriteLineAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).AsMemory(), timeout.Token);
    }

    private object ReceiveEvent(CaptureEvent? value)
    {
        if (value is null) throw new ArgumentException("Missing capture event.");
        _capture.Receive(value);
        return new { received = true };
    }
}
