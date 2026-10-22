using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using GuideMe.Instructor;
using GuideMe.Instructor.Web;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 53922));
var app = builder.Build();
var sourceAssets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
var webRoot = Directory.Exists(sourceAssets) ? sourceAssets : Path.Combine(AppContext.BaseDirectory, "wwwroot");
var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var capture = new InstructorCaptureBridge();
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
        "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/capture"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin != InstructorCaptureBridge.ExtensionOrigin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        context.Response.Headers["Access-Control-Allow-Origin"] = InstructorCaptureBridge.ExtensionOrigin;
        context.Response.Headers["Vary"] = "Origin";
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-GuideMe-Capture-Token";
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        if (context.Request.ContentLength is > 16384)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        if (context.Request.Path != "/capture/pair" &&
            !capture.Verify(context.Request.Headers["X-GuideMe-Capture-Token"].ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }
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

app.MapPost("/capture/pair", () =>
{
    var token = capture.Pair();
    return token is null ? Results.StatusCode(403) : Results.Ok(new { token });
});
app.MapPost("/capture/heartbeat", (CaptureTab[] tabs) => Results.Ok(capture.Heartbeat(tabs)));
app.MapPost("/capture/event", (CaptureEvent body) =>
{
    capture.Receive(body);
    return Results.NoContent();
});
app.MapPost("/api/capture/pair", () => { capture.OpenPairing(); return Results.Ok(capture.Status()); });
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
        rules = TableRows(V5GuideReader.ReadGuideTable(id, "StepRules")).Where(r => Equals(r["StepId"], stepId))
    });
});
app.MapPost("/api/guides", (NewGuide body) =>
    Results.Ok(V5GuideWriter.CreateGuideWithContext(body.Name, body.ContextId,
        body.Runtime, body.MatcherProperty, body.MatcherValue)));
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
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(localOrigin)
        { UseShellExecute = true });
    }
    catch (Exception ex) { Console.Error.WriteLine($"Open {localOrigin} manually: {ex.Message}"); }
});
Console.WriteLine($"GuideMe Instructor Web: {localOrigin}");
app.Run();

static List<Dictionary<string, object?>> TableRows(DataTable table) =>
    table.Rows.Cast<DataRow>()
        .Select(row => table.Columns.Cast<DataColumn>()
            .ToDictionary(col => col.ColumnName,
                col => row.IsNull(col) ? null : row[col], StringComparer.Ordinal))
        .ToList();

internal sealed record NewGuide(string Name, string ContextId, string Runtime,
    string MatcherProperty, string MatcherValue);
internal sealed record RenameGuide(string Name);
internal sealed record SaveContext(string ContextId, string Runtime,
    string MatcherProperty, string MatcherValue);

