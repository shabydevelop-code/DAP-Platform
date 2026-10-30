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
var windowsPicker = new WindowsTargetPicker();
app.Lifetime.ApplicationStopping.Register(windowsPicker.Dispose);
app.MapPost("/api/windows/picker/start", (WindowsInspectRequest request) =>
{
    // Resolve the selected application before creating the non-activating overlay.
    using var targetProcess = Process.GetProcessById(request.ProcessId);
    var instructorWindow = WindowsTargetPicker.CaptureInstructorWindow();
    var activated = GuideMe.Runtime.Windows.Targets.WindowsWindowActivation.Activate(targetProcess.MainWindowHandle);
    var status = windowsPicker.Start(request.ProcessId, instructorWindow);
    return Results.Ok(new { status.Picking, status.Picked, status.Selection,
        status.Error, Activated = activated });
});
app.MapGet("/api/windows/picker/status", () => Results.Ok(windowsPicker.Status()));
app.MapPost("/api/windows/picker/cancel", () => Results.Ok(windowsPicker.Stop()));

// Single API for primary target confirmation and V5 persistence. Runtime-specific
// selection is verified here; the DB writer never branches into per-runtime paths.
app.MapPost("/api/guides/{id:long}/steps/{stepId}/primary-target",
    (long id, string stepId, SavePrimaryTargetRequest request) =>
{
    var steps = V5GuideReader.ReadGuideTable(id, "Steps");
    var step = steps.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["StepId"].ToString(), stepId, StringComparison.Ordinal));
    if (step is null) return Results.NotFound();
    var contextId = step["ApplicationContextId"].ToString() ?? "";
    var contexts = V5GuideReader.ReadGuideTable(id, "ApplicationContexts");
    var context = contexts.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["ContextId"].ToString(), contextId, StringComparison.Ordinal));
    if (context is null) return Results.BadRequest(new { message = "לא נמצא שיוך אפליקציה לשלב." });
    var runtime = context["Runtime"].ToString();
    IReadOnlyList<V5GuideWriter.TargetLocator> locators;
    IReadOnlyList<V5GuideWriter.TargetLocator> frames;
    string? displayName;
    if (runtime == "Web")
    {
        var target = capture.GetApprovedTarget(id, contextId, request.GridChoice);
        locators = new[] { new V5GuideWriter.TargetLocator("css", target.Selector) };
        frames = target.FramePath.Select(f => new V5GuideWriter.TargetLocator("css", f)).ToArray();
        displayName = target.DisplayName;
    }
    else if (runtime == "Windows")
    {
        if (request.ProcessId is not > 0)
            return Results.BadRequest(new { message = "תהליך Windows חסר." });
        var picked = windowsPicker.Status().Selection;
        if (picked is null || picked.Error is not null || picked.ProcessId != request.ProcessId.Value)
            return Results.BadRequest(new { message = "יש לבחור תחילה רכיב Windows תקין בממפה." });
        using var process = Process.GetProcessById(request.ProcessId.Value);
        using var configuredMatchers = JsonDocument.Parse(context["MatchersJson"].ToString() ?? "[]");
        var allowedProcess = configuredMatchers.RootElement.EnumerateArray().Any(m =>
        {
            if (!m.TryGetProperty("Kind", out var kind) ||
                !m.TryGetProperty("Value", out var value)) return false;
            var name = value.GetString();
            return kind.GetString() == "ProcessName" && !string.IsNullOrWhiteSpace(name) &&
                string.Equals(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? name[..^4] : name, process.ProcessName, StringComparison.OrdinalIgnoreCase);
        });
        if (!allowedProcess)
            return Results.BadRequest(new { message = "הרכיב אינו שייך לאפליקציה שהוגדרה בלומדה." });
        locators = picked.Locators
            .Select(l => new V5GuideWriter.TargetLocator(l.Strategy, l.Value)).ToArray();
        frames = Array.Empty<V5GuideWriter.TargetLocator>();
        displayName = picked.Name;
    }
    else return Results.BadRequest(new { message = "סביבת יעד לא נתמכת." });

    try
    {
        V5GuideWriter.SavePrimaryTarget(id, stepId, runtime, locators, frames,
            displayName, request.Condition, request.Replacing);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex)
    {
        app.Logger.LogError(ex, "Primary target persistence failed for guide {GuideId}, step {StepId}, runtime {Runtime}", id, stepId, runtime);
        return Results.Problem("שמירת הרכיב נכשלה במסד הנתונים. יש לבדוק את יומן השרת.", statusCode: 500);
    }
    if (runtime == "Web") capture.CancelPicking();
    else windowsPicker.Stop();
    return Results.NoContent();
});

app.MapPost("/api/guides/{id:long}/steps/{stepId}/primary-target/variable",
    (long id, string stepId, BindPrimaryTargetVariableRequest request) =>
{
    try
    {
        V5GuideWriter.SavePrimaryTargetLocatorTemplate(id, stepId, request.LocatorValue);
        return Results.NoContent();
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

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
app.MapPost("/api/guides/{id:long}/steps/{stepId}/completion-target",
    (long id, string stepId, SaveCompletionTargetRequest request) =>
{
    var rows = V5GuideReader.ReadGuideTable(id, "Steps");
    var row = rows.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["StepId"].ToString(), stepId, StringComparison.Ordinal));
    if (row is null) return Results.NotFound();
    var contextId = row["ApplicationContextId"].ToString() ?? "";
    var contexts = V5GuideReader.ReadGuideTable(id, "ApplicationContexts");
    var context = contexts.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["ContextId"].ToString(), contextId, StringComparison.Ordinal));
    if (context is null) return Results.BadRequest(new { message = "לא נמצא שיוך אפליקציה." });
    var runtime = context["Runtime"].ToString();
    if (runtime == "Windows")
    {
        if (request.Kind is not ("value-contains" or "target-appeared" or "target-vanished") || request.ProcessId is not > 0)
            return Results.BadRequest(new { message = "יש לבחור בדיקת ערך ורכיב Windows תקין." });
        var picked = windowsPicker.Status().Selection;
        if (picked is null || picked.Error is not null || picked.ProcessId != request.ProcessId.Value)
            return Results.BadRequest(new { message = "לא נבחר רכיב Windows תקין." });
        using var process = Process.GetProcessById(request.ProcessId.Value);
        using var matchers = JsonDocument.Parse(context["MatchersJson"].ToString() ?? "[]");
        var allowed = matchers.RootElement.EnumerateArray().Any(m =>
            m.TryGetProperty("Kind", out var kind) && kind.GetString() == "ProcessName" &&
            m.TryGetProperty("Value", out var value) &&
            string.Equals((value.GetString() ?? "").Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
                process.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (!allowed) return Results.BadRequest(new { message = "הרכיב אינו שייך לאפליקציה שהוגדרה." });
        try
        {
            V5GuideWriter.SaveWebCompletionTarget(id, stepId, "", Array.Empty<string>(),
                request.Kind, picked.Name, expectedValue: request.ExpectedValue,
                runtime: "Windows", windowsLocators: picked.Locators
                    .Select(l => new V5GuideWriter.TargetLocator(l.Strategy, l.Value)).ToArray());
            windowsPicker.Stop();
            return Results.NoContent();
        }
        catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: 400); }
        catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
    }
    if (runtime != "Web") return Results.BadRequest(new { message = "סביבת יעד לא נתמכת." });
    var selected = capture.GetApprovedTarget(id, contextId);
    try
    {
        var capturedState = request.Kind == "attribute-equals"
            ? capture.GetApprovedStateAttribute(id, row["ApplicationContextId"].ToString() ?? "",
                request.StateAttributeIndex ?? -1)
            : ((string Name, string Value, string Selector)?)null;
        V5GuideWriter.SaveWebCompletionTarget(id, stepId,
            capturedState?.Selector ?? selected.Selector,
            selected.FramePath, request.Kind, selected.DisplayName,
            capturedState?.Name, capturedState?.Value, request.VariableId, request.SelectedTag, request.ExpectedValue);
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
app.MapDelete("/api/guides/{id:long}/steps/{stepId}/primary-target",
    (long id, string stepId) =>
{
    V5GuideWriter.RemovePrimaryTarget(id, stepId);
    return Results.NoContent();
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
    V5GuideWriter.UpdatePrimaryValidation(id, stepId, request.Kind, request.ExpectedValue);
    return Results.NoContent();
});

app.MapDelete("/api/guides/{id:long}/steps/{stepId}/validation",
    (long id, string stepId) =>
{
    V5GuideWriter.RemovePrimaryValidation(id, stepId);
    return Results.NoContent();
});

// The common test action needs Chrome transport only when an enabled Web step
// will execute. Windows-only guides never depend on a browser extension.
app.MapGet("/api/guides/{id:long}/test-requirements", (long id, string? startStepId) =>
{
    if (V5GuideReader.ListGuides().All(g => g.Id != id)) return Results.NotFound();
    var steps = V5GuideReader.ReadGuideTable(id, "Steps").Rows.Cast<DataRow>()
        .OrderBy(r => Convert.ToInt32(r["StepOrder"])).ToArray();
    var start = startStepId is null ? 0 :
        Array.FindIndex(steps, s => string.Equals(s["StepId"].ToString(), startStepId, StringComparison.Ordinal));
    if (start < 0) return Results.BadRequest(new { message = "שלב ההתחלה אינו קיים." });
    var webContexts = V5GuideReader.ReadGuideTable(id, "ApplicationContexts")
        .Rows.Cast<DataRow>().Where(r => r["Runtime"].ToString() == "Web")
        .Select(r => r["ContextId"].ToString()).ToHashSet(StringComparer.Ordinal);
    var requiresChromeExtension = steps.Skip(start).Any(s =>
        Convert.ToInt64(s["IsEnabled"]) == 1 &&
        webContexts.Contains(s["ApplicationContextId"].ToString()));
    return Results.Ok(new { requiresChromeExtension });
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
    // A step without a primary target is a manual explanation step.
    // A targeted step must have exactly one primary validation before testing.
    var ordered = V5GuideReader.ReadGuideTable(id, "Steps").Rows.Cast<DataRow>()
        .OrderBy(row => Convert.ToInt32(row["StepOrder"])).ToArray();
    var startIndex = startStepId is null ? 0 : Array.FindIndex(ordered,
        row => string.Equals(row["StepId"].ToString(), startStepId, StringComparison.Ordinal));
    var targets = V5GuideReader.ReadGuideTable(id, "StepTargets").Rows.Cast<DataRow>()
        .Where(row => string.Equals(row["Purpose"].ToString(), "Primary", StringComparison.OrdinalIgnoreCase))
        .GroupBy(row => row["StepId"].ToString() ?? "")
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    var validations = V5GuideReader.ReadGuideTable(id, "StepRules").Rows.Cast<DataRow>()
        .Where(row => string.Equals(row["RuleType"].ToString(), "PrimaryValidation", StringComparison.Ordinal))
        .GroupBy(row => row["StepId"].ToString() ?? "")
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    foreach (var row in ordered.Skip(Math.Max(startIndex, 0)))
    {
        if (Convert.ToInt64(row["IsEnabled"]) != 1) continue;
        var stepKey = row["StepId"].ToString() ?? "";
        var primaryCount = targets.GetValueOrDefault(stepKey);
        var validationCount = validations.GetValueOrDefault(stepKey);
        if (primaryCount == 0 && validationCount == 0) continue;
        if (primaryCount != 1 || validationCount != 1)
            return Results.Problem("לא ניתן לבדוק את שלב " + row["StepOrder"] +
                ": נבחר רכיב במסך אך לא הוגדר תנאי התקדמות תקין. יש להשלים את ההגדרה לפני הבדיקה.",
                statusCode: 409);
    }
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

app.MapGet("/api/guides/{id:long}/variables", (long id) =>
    Results.Ok(V5VariableCatalog.List(id)));
app.MapPost("/api/guides/{id:long}/variables", (long id, GuideVariableRequest body) =>
    Results.Ok(V5VariableCatalog.Add(id, body.Name, body.InitialValue)));
app.MapPut("/api/guides/{id:long}/variables/{variableId}",
    (long id, string variableId, GuideVariableRequest body) =>
{
    V5VariableCatalog.Update(id, variableId, body.Name, body.InitialValue);
    return Results.NoContent();
});
app.MapDelete("/api/guides/{id:long}/variables/{variableId}",
    (long id, string variableId) =>
{
    V5VariableCatalog.Delete(id, variableId);
    return Results.NoContent();
});

app.MapPost("/api/guides/{id:long}/steps/{stepId}/variable-source",
    (long id, string stepId, SaveVariableSourceRequest request) =>
{
    var steps = V5GuideReader.ReadGuideTable(id, "Steps");
    var step = steps.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["StepId"].ToString(), stepId, StringComparison.Ordinal));
    if (step is null) return Results.NotFound();
    var contextId = step["ApplicationContextId"].ToString() ?? "";
    var contexts = V5GuideReader.ReadGuideTable(id, "ApplicationContexts");
    var context = contexts.Rows.Cast<DataRow>().FirstOrDefault(r =>
        string.Equals(r["ContextId"].ToString(), contextId, StringComparison.Ordinal));
    if (context is null) return Results.BadRequest(new { message = "לא נמצאה אפליקציית יעד לשלב." });
    var runtime = context["Runtime"].ToString();
    IReadOnlyList<V5GuideWriter.TargetLocator> locators;
    IReadOnlyList<V5GuideWriter.TargetLocator> frames;
    string? displayName;
    if (runtime == "Web")
    {
        var target = capture.GetApprovedTarget(id, contextId, request.GridChoice);
        locators = new[] { new V5GuideWriter.TargetLocator("css", target.Selector) };
        frames = target.FramePath.Select(f => new V5GuideWriter.TargetLocator("css", f)).ToArray();
        displayName = target.DisplayName;
    }
    else if (runtime == "Windows")
    {
        if (request.ProcessId is not > 0)
            return Results.BadRequest(new { message = "תהליך Windows חסר." });
        var picked = windowsPicker.Status().Selection;
        if (picked is null || picked.Error is not null || picked.ProcessId != request.ProcessId.Value)
            return Results.BadRequest(new { message = "יש לבחור תחילה רכיב Windows תקין." });
        using var process = Process.GetProcessById(request.ProcessId.Value);
        using var matchers = JsonDocument.Parse(context["MatchersJson"].ToString() ?? "[]");
        var allowed = matchers.RootElement.EnumerateArray().Any(m =>
            m.TryGetProperty("Kind", out var kind) && kind.GetString() == "ProcessName"
            && m.TryGetProperty("Value", out var value)
            && string.Equals((value.GetString() ?? "").Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
                process.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            return Results.BadRequest(new { message = "הרכיב אינו שייך לאפליקציית היעד." });
        var selected = picked.Locators.FirstOrDefault(l =>
            l.Strategy == "automation-id" && !string.IsNullOrWhiteSpace(l.Value))
            ?? picked.Locators.FirstOrDefault(l =>
                l.Strategy == "name" && !string.IsNullOrWhiteSpace(l.Value));
        if (selected is null)
            return Results.BadRequest(new { message = "לא נמצא מזהה Windows מתאים לרכיב." });
        locators = new[] { new V5GuideWriter.TargetLocator(selected.Strategy, selected.Value) };
        frames = Array.Empty<V5GuideWriter.TargetLocator>();
        displayName = picked.Name;
    }
    else return Results.BadRequest(new { message = "סביבת היעד אינה נתמכת." });
    V5VariableCatalog.AddPickedRead(id, stepId, request.VariableId,
        runtime!, locators, frames, displayName, request.Property, request.Pattern);
    if (runtime == "Web") capture.CancelPicking();
    else windowsPicker.Stop();
    return Results.NoContent();
});

app.MapGet("/api/guides/{id:long}/steps/{stepId}/variables", (long id, string stepId) =>
    Results.Ok(V5VariableCatalog.ListActions(id, stepId)));
app.MapPost("/api/guides/{id:long}/steps/{stepId}/variables",
    (long id, string stepId, StepVariableActionRequest body) =>
{
    V5VariableCatalog.AddAction(id, stepId, body.VariableId, body.Kind,
        body.Value, body.SourceVariableId);
    return Results.NoContent();
});
app.MapDelete("/api/guides/{id:long}/steps/{stepId}/variables/{actionId}",
    (long id, string stepId, string actionId) =>
{
    V5VariableCatalog.DeleteAction(id, stepId, actionId);
    return Results.NoContent();
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
        anchors = TableRows(V5GuideReader.ReadGuideTable(id, "ElementAnchors")),
        rules = TableRows(V5GuideReader.ReadGuideTable(id, "StepRules")).Where(r => Equals(r["StepId"], stepId))
    });
});
app.MapPost("/api/guides/{id:long}/steps", (long id, NewManualStep request) =>
    Results.Ok(new { stepId = V5GuideWriter.CreateManualStep(id,
        request.ContextId, request.Bubble, request.Placement) }));
app.MapPost("/api/guides/{id:long}/steps/{stepId}/duplicate", (long id, string stepId) =>
{
    if (V5GuideReader.ListSteps(id).All(step => step.Id != stepId))
        return Results.NotFound();
    return Results.Ok(new { stepId = V5GuideWriter.DuplicateStep(id, stepId) });
});
app.MapPut("/api/guides/{id:long}/steps/{stepId}", (long id, string stepId, EditStep body) =>
{
    V5GuideWriter.UpdateStep(id, stepId, body.Bubble, body.Enabled, body.ContextId);
    return Results.NoContent();
});
app.MapDelete("/api/guides/{id:long}/steps/{stepId}", (long id, string stepId) =>
{
    if (V5GuideReader.ListSteps(id).All(step => step.Id != stepId))
        return Results.NotFound();
    V5GuideWriter.DeleteStep(id, stepId);
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
    // A referenced application is a conflict, not an internal server error.
    // Keep the database-side transactional guard authoritative.
    if (V5GuideWriter.CountApplicationSteps(id, contextId) > 0)
        return Results.Conflict(new { message = "האפליקציה משויכת לשלבים בלומדה. יש להסיר תחילה את השיוכים." });
    try
    {
        V5GuideWriter.DeleteApplication(id, contextId);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
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

internal sealed record WindowsInspectRequest(int ProcessId, bool Focused = false);
internal sealed record SavePrimaryTargetRequest(string Condition, bool Replacing = false,
    int? ProcessId = null, int? GridChoice = null);
internal sealed record PickStart(long GuideId, string ContextId);
internal sealed record SaveCompletionTargetRequest(string Kind, int? StateAttributeIndex = null,
    string? VariableId = null, string? SelectedTag = null, string? ExpectedValue = null,
    int? ProcessId = null);

internal sealed record EditWebValidation(string Kind, string? ExpectedValue);

internal sealed record GuideVariableRequest(string Name, string? InitialValue);

internal sealed record StepVariableActionRequest(string VariableId, string Kind, string? Value,
    string? SourceVariableId);


internal sealed record SaveVariableSourceRequest(string VariableId, string Property,
    int? ProcessId, int? GridChoice, string? Pattern);


internal sealed record IdentifiedActionRequest(string VariableId);

internal sealed record BindPrimaryTargetVariableRequest(string LocatorValue);
