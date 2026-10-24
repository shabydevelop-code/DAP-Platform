using System.Data;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc;
using GuideMe.Instructor.Web.Data;
using GuideMe.Instructor.Web;

// Enforce a single server before Kestrel and the native pipe are created.
using var instanceMutex = InstructorSingleInstance.TryAcquire();
if (instanceMutex is null)
{
    if (!InstructorSingleInstance.ActivateExisting())
        Console.WriteLine("Instructor is already running in this Windows session. Activate its existing window.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 53922));
var app = builder.Build();
app.UseWebSockets();
// Resolve assets from the executable in both dotnet run and published deployment.
// The current working directory is not necessarily the application's directory.
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!File.Exists(Path.Combine(webRoot, "index.html")))
    webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
if (!File.Exists(Path.Combine(webRoot, "index.html")))
    throw new FileNotFoundException("GuideMe Instructor Web UI assets are missing. Re-publish the application.", Path.Combine(webRoot, "index.html"));
var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var capture = new InstructorCaptureBridge();
var nativePipe = new InstructorNativePipe(capture);
var windowLifetime = new InstructorWindowLifetime(app.Lifetime);
const string localOrigin = "http://127.0.0.1:53922";

app.Use(async (context, next) =>
{
    if (!string.Equals(context.Request.Host.Value, "127.0.0.1:53922", StringComparison.Ordinal) ||
        context.Connection.RemoteIpAddress is not { } client || !IPAddress.IsLoopback(client))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' ws://127.0.0.1:53922; img-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        if ((!string.IsNullOrEmpty(origin) && !string.Equals(origin, localOrigin, StringComparison.Ordinal)) ||
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(context.Request.Headers["X-GuideMe-Session"].ToString()),
                System.Text.Encoding.UTF8.GetBytes(sessionToken)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!HttpMethods.IsGet(context.Request.Method) &&
            !string.Equals(context.Request.ContentType?.Split(';')[0], "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
    }
    await next();
});

// A live WebSocket belongs to the Instructor UI window. The server stops only
// after the last UI connection closes; a short grace period allows reloads.
app.Map("/window-session", async context =>
{
    if (!string.Equals(context.Request.Headers.Origin.ToString(), localOrigin, StringComparison.Ordinal) ||
        !context.WebSockets.IsWebSocketRequest ||
        !string.Equals(context.Request.Query["token"].ToString(), sessionToken, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    // Kestrel waits for upgraded WebSocket requests during graceful shutdown.
    // RequestAborted alone is not cancelled until the browser disconnects,
    // which can leave PowerShell stuck indefinitely at "Shutting down".
    using var closing = CancellationTokenSource.CreateLinkedTokenSource(
        context.RequestAborted, app.Lifetime.ApplicationStopping);
    windowLifetime.Connected();
    try
    {
        var buffer = new byte[128];
        while (socket.State == WebSocketState.Open && !closing.IsCancellationRequested)
        {
            var message = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), closing.Token);
            if (message.MessageType == WebSocketMessageType.Close) break;
        }
    }
    catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
    catch (WebSocketException) { }
    finally
    {
        if (app.Lifetime.ApplicationStopping.IsCancellationRequested)
            socket.Abort();
        windowLifetime.Disconnected();
    }
});

app.MapGet("/api/discovery", () => Results.Ok(new
{
    webConnected = capture.IsConnected,
    web = capture.GetOpenTabs(),
    windows = InstructorApplicationDiscovery.Windows()
}));

// Read-only Windows target discovery. The caller chooses a PID from /api/discovery.
// No target is persisted until the author explicitly approves a supported locator.
app.MapPost("/api/windows/inspect", (WindowsInspectRequest request) =>
{
    try { return Results.Ok(WindowsTargetInspector.Inspect(request.ProcessId, request.Focused)); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                               System.Windows.Automation.ElementNotAvailableException or
                               System.Runtime.InteropServices.COMException)
    { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapGet("/api/capture/status", () => Results.Ok(capture.Status()));
app.MapPost("/api/capture/select", (CaptureSelection body) =>
{
    var guide = V5GuideReader.ListGuides().FirstOrDefault(g => g.Id == body.GuideId);
    if (guide is null) return Results.NotFound();
    var rows = V5GuideReader.ReadGuideTable(body.GuideId, "ApplicationContexts");
    var row = rows.Rows.Cast<DataRow>().FirstOrDefault(x =>
        string.Equals(x["ContextId"].ToString(), body.ContextId, StringComparison.Ordinal));
    if (row is null || row["Runtime"].ToString() != "Web") return Results.BadRequest();
    return Results.Ok(capture.Select(body.GuideId, body.ContextId, row["MatchersJson"].ToString()!, body.TabId));
});
app.MapPost("/api/capture/start", (CaptureStart body) =>
{
    capture.Begin(body.GuideId, body.ContextId);
    return Results.Ok(capture.Status());
});
app.MapPost("/api/capture/stop", () => Results.Ok(new { events = capture.End() }));

app.MapPost("/api/target-picker/start", (PickStart request) =>
    Results.Ok(capture.StartPicking(request.GuideId, request.ContextId)));
app.MapPost("/api/target-picker/cancel", () => Results.Ok(capture.CancelPicking()));
app.MapPost("/api/guides/{id:long}/steps/{stepId}/web-target",
    (long id, string stepId, SaveWebTargetRequest request) =>
{
    var rows = V5GuideReader.ReadGuideTable(id, "Steps");
    var row = rows.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["StepId"].ToString(), stepId, StringComparison.Ordinal));
    if (row is null) return Results.NotFound();
    var contextId = row["ApplicationContextId"].ToString() ?? "";
    var selected = capture.GetApprovedTarget(id, contextId, request.GridChoice);
    V5GuideWriter.SaveWebTarget(id, stepId, selected.Selector, selected.FramePath, request.Condition, selected.DisplayName);
    capture.CancelPicking();
    return Results.NoContent();
});

app.MapPut("/api/guides/{id:long}/steps/{stepId}/web-target", (long id, string stepId, ReplaceWebTargetRequest request) =>
{
    var rows = V5GuideReader.ReadGuideTable(id, "Steps");
    var row = rows.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["StepId"].ToString(), stepId, StringComparison.Ordinal));
    if (row is null) return Results.NotFound();
    var selected = capture.GetApprovedTarget(id, row["ApplicationContextId"].ToString() ?? "", request.GridChoice);
    V5GuideWriter.ReplaceWebTarget(id, stepId, selected.Selector, selected.FramePath, selected.DisplayName);
    capture.CancelPicking();
    return Results.NoContent();
});

app.MapPost("/api/guides/{id:long}/steps/{stepId}/completion-target",
    (long id, string stepId, SaveCompletionTargetRequest request) =>
{
    var rows = V5GuideReader.ReadGuideTable(id, "Steps");
    var row = rows.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["StepId"].ToString(), stepId, StringComparison.Ordinal));
    if (row is null) return Results.NotFound();
    var selected = capture.GetApprovedTarget(id, row["ApplicationContextId"].ToString() ?? "");
    try
    {
        var capturedState = request.Kind == "attribute-equals"
            ? capture.GetApprovedStateAttribute(id, row["ApplicationContextId"].ToString() ?? "",
                request.StateAttributeIndex ?? -1)
            : ((string Name, string Value, string Selector)?)null;
        V5GuideWriter.SaveWebCompletionTarget(id, stepId,
            capturedState?.Selector ?? selected.Selector,
            selected.FramePath, request.Kind, selected.DisplayName,
            capturedState?.Name, capturedState?.Value);
        capture.CancelPicking();
        return Results.NoContent();
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 409);
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex)
    {
        app.Logger.LogError(ex, "Failed to save Web completion target for guide {GuideId}, step {StepId}", id, stepId);
        return Results.Problem("שמירת תנאי ההשלמה נכשלה במסד הנתונים. בדוק את הודעת השגיאה בחלון ה־Instructor.", statusCode: 500);
    }
});
app.MapDelete("/api/guides/{id:long}/steps/{stepId}/completion-target",
    (long id, string stepId) =>
{
    V5GuideWriter.RemoveWebCompletionTarget(id, stepId);
    return Results.NoContent();
});

app.MapPut("/api/guides/{id:long}/steps/{stepId}/validation",
    (long id, string stepId, EditWebValidation request) =>
{
    V5GuideWriter.UpdateWebPrimaryValidation(id, stepId, request.Kind, request.ExpectedValue);
    return Results.NoContent();
});

// Test the persisted guide with the production Learner, optionally starting
// at an editor-selected step. No preceding application actions are replayed.
app.MapPost("/api/guides/{id:long}/test", (long id, string? startStepId) =>
{
    var guide = V5GuideReader.ListGuides().FirstOrDefault(g => g.Id == id);
    if (guide is null) return Results.NotFound();
    var enabledSteps = V5GuideReader.ListSteps(id).Where(step => step.Enabled).ToArray();
    if (enabledSteps.Length == 0)
        return Results.Problem("בלומדה אין שלבים פעילים.", statusCode: 409);
    if (startStepId is not null && !enabledSteps.Any(step => step.Id == startStepId))
        return Results.Problem("שלב ההתחלה אינו קיים או אינו פעיל.", statusCode: 400);
    const string learnerExe = @"C:\GuideMe\GuideMe.exe";
    if (!File.Exists(learnerExe))
        return Results.Problem("לא נמצאה התקנת Learner ב־C:\\GuideMe\\GuideMe.exe. פרסם את GuideMe תחילה.", statusCode: 409);
    try
    {
        var launch = new ProcessStartInfo(learnerExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(learnerExe)!
        };
        launch.ArgumentList.Add("--guide");
        launch.ArgumentList.Add(guide.Key);
        if (startStepId is not null)
        {
            launch.ArgumentList.Add("--start-step");
            launch.ArgumentList.Add(startStepId);
        }
        using var process = Process.Start(launch);
        if (process is null)
            return Results.Problem("לא ניתן להפעיל את ה־Learner.", statusCode: 500);
        return Results.Ok(new { started = true, guideId = id });
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
        app.Logger.LogError(ex, "Could not launch Learner for guide {GuideId}", id);
        return Results.Problem("הפעלת ה־Learner נכשלה. בדוק את חלון ה־Instructor.", statusCode: 500);
    }
});

app.MapGet("/api/guides", () => Results.Ok(V5GuideReader.ListGuides()));
app.MapGet("/api/guides/{id:long}", (long id) =>
{
    var guide = V5GuideReader.ListGuides().FirstOrDefault(g => g.Id == id);
    return guide is null ? Results.NotFound() : Results.Ok(new
    {
        guide, steps = V5GuideReader.ListSteps(id),
        contexts = TableRows(V5GuideReader.ReadGuideTable(id, "ApplicationContexts"))
    });
});
app.MapGet("/api/guides/{id:long}/step/{stepId}", (long id, string stepId) =>
{
    if (V5GuideReader.ListSteps(id).All(s => s.Id != stepId)) return Results.NotFound();
    return Results.Ok(new
    {
        step = TableRows(V5GuideReader.ReadGuideTable(id, "Steps")).Where(r => Equals(r["StepId"], stepId)),
        targets = TableRows(V5GuideReader.ReadGuideTable(id, "StepTargets")).Where(r => Equals(r["StepId"], stepId)),
        elements = TableRows(V5GuideReader.ReadGuideTable(id, "Elements")),
        rules = TableRows(V5GuideReader.ReadGuideTable(id, "StepRules")).Where(r => Equals(r["StepId"], stepId))
    });
});
app.MapPost("/api/guides/{id:long}/steps", (long id, NewManualStep request) =>
    Results.Ok(new { stepId = V5GuideWriter.CreateManualStep(id,
        request.ContextId, request.Bubble, request.Placement) }));
app.MapPut("/api/guides/{id:long}/steps/{stepId}", (long id, string stepId, EditStep body) =>
{
    V5GuideWriter.UpdateStep(id, stepId, body.Bubble, body.Enabled, body.ContextId);
    return Results.NoContent();
});
app.MapPost("/api/guides", (CreateGuideRequest body) =>
    Results.Ok(V5GuideWriter.CreateGuideWithApplications(body.Name, body.Applications)));
app.MapPut("/api/guides/{id:long}/name", (long id, RenameGuide body) =>
    Results.Ok(V5GuideWriter.RenameGuide(id, body.Name)));
app.MapPost("/api/guides/{id:long}/contexts", (long id, SaveContext body) =>
{
    V5GuideWriter.SaveApplicationContext(id, body.ContextId, body.Runtime, body.MatcherProperty, body.MatcherValue);
    return Results.NoContent();
});
app.MapPut("/api/guides/{id:long}/contexts/{contextId}/name",
    (long id, string contextId, RenameGuide body) =>
{
    V5GuideWriter.RenameApplication(id, contextId, body.Name);
    return Results.NoContent();
});
// Reuse the WPF Instructor's transactional V5 deletion implementation.
// An exact-name confirmation prevents accidental deletion of a different guide.
app.MapDelete("/api/guides/{id:long}", (long id, [FromBody] DeleteGuideConfirmation request) =>
{
    var guide = V5GuideReader.ListGuides().FirstOrDefault(g => g.Id == id);
    if (guide is null) return Results.NotFound();
    if (!string.Equals(request.Name, guide.Name, StringComparison.Ordinal))
        return Results.BadRequest(new { message = "שם הלומדה שהוזן אינו תואם." });
    V5GuideWriter.DeleteGuide(id);
    return Results.NoContent();
});

app.MapDelete("/api/guides/{id:long}/contexts/{contextId}",
    (long id, string contextId) =>
{
    V5GuideWriter.DeleteApplication(id, contextId);
    return Results.NoContent();
});

// The token is emitted only into this same-origin document, not the API.
app.MapGet("/", () =>
{
    var html = File.ReadAllText(Path.Combine(webRoot, "index.html"));
    return Results.Content(html.Replace("__SESSION_TOKEN__", sessionToken, StringComparison.Ordinal),
        "text/html; charset=utf-8");
});
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot) });

app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        // Chrome --app opens a dedicated window in the existing browser profile:
        // the Instructor extension remains installed and ordinary tabs unaffected.
        var chrome = InstructorWindowLifetime.FindChromeExecutable();
        var start = chrome is null
            ? new ProcessStartInfo(localOrigin) { UseShellExecute = true }
            : new ProcessStartInfo(chrome)
              {
                  UseShellExecute = false,
                  ArgumentList = { "--start-maximized", "--app=" + localOrigin }
              };
        Process.Start(start);
        if (chrome is not null)
            _ = Task.Run(InstructorSingleInstance.MaximizeInstructorWindow);
        Console.WriteLine(chrome is null
            ? "Chrome not found; opened default browser. Closing the UI ends Instructor."
            : "Instructor opened in a maximized Chrome app window.");
    }
    catch (Exception ex) { Console.Error.WriteLine($"Open {localOrigin} manually: {ex.Message}"); }
});
Console.WriteLine($"GuideMe Instructor Web: {localOrigin}");
var pipeTask = nativePipe.RunAsync(app.Lifetime.ApplicationStopping);
app.Run();
await pipeTask;

static List<Dictionary<string, object?>> TableRows(DataTable table) =>
    table.Rows.Cast<DataRow>()
        .Select(row => table.Columns.Cast<DataColumn>()
            .ToDictionary(col => col.ColumnName,
                col => row.IsNull(col) ? null : row[col], StringComparer.Ordinal))
        .ToList();

internal sealed record CreateGuideRequest(string Name,
    List<V5GuideWriter.SelectedApplication> Applications);
internal sealed record RenameGuide(string Name);
internal sealed record SaveContext(string ContextId, string Runtime,
    string MatcherProperty, string MatcherValue);


internal sealed record DeleteGuideConfirmation(string Name);
internal sealed record EditStep(string? Bubble, bool Enabled, string? ContextId);

internal sealed record NewManualStep(string ContextId, string Bubble, string Placement);

internal sealed record PickStart(long GuideId, string ContextId);
internal sealed record SaveWebTargetRequest(string Condition, int? GridChoice = null);
internal sealed record ReplaceWebTargetRequest(int? GridChoice = null);
internal sealed record SaveCompletionTargetRequest(string Kind, int? StateAttributeIndex = null);

internal sealed record EditWebValidation(string Kind, string? ExpectedValue);
