var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var backendUrl = builder.Configuration["TestCrmBackendUrl"] ?? "http://localhost:5201";
var backend = new HttpClient { BaseAddress = new Uri(backendUrl) };

app.Map("/api/{**path}", async context =>
{
    var target = context.Request.Path + context.Request.QueryString;
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

    foreach (var header in context.Request.Headers)
        if (!string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

    if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
    }

    using var response = await backend.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;

    foreach (var header in response.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();
    foreach (var header in response.Content.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    context.Response.Headers.Remove("transfer-encoding");
    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});

app.Run();
