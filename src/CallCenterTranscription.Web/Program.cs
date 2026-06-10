using CallCenterTranscription.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.Configure<BackendApiOptions>(builder.Configuration.GetSection(BackendApiOptions.SectionName));
builder.Services.AddHttpClient<PipelineApiClient>();
builder.Services.AddHttpClient("rep-proxy");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Force revalidation of HTML document responses to prevent stale UI after deploys.
// The OnStarting callback checks Content-Type at response-flush time so static asset
// responses (text/css, application/javascript, etc.) are not affected.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var ct = context.Response.ContentType;
        if (ct != null && ct.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
        return Task.CompletedTask;
    });
    await next();
});

app.UseRouting();

app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// ── Rep softphone proxy ───────────────────────────────────────────────────────────────────────
// Same-origin shim so the browser never holds the backend shared secret. These forward to the
// API's /api/rep/* routes injecting the X-Rep-Key header (from Rep:AccessKey). The browser calls
// /rep/token and /rep/register on this Web origin only.
static (string? baseUrl, string? key) RepProxyConfig(IConfiguration cfg) =>
    (cfg[$"{BackendApiOptions.SectionName}:BaseUrl"], cfg["Rep:AccessKey"]);

app.MapGet("/rep/token", async (
    HttpContext http,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    CancellationToken ct) =>
{
    var (baseUrl, key) = RepProxyConfig(cfg);
    if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        return Results.Problem("Rep softphone backend is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var userId = http.Request.Query["userId"].ToString();
    var target = new Uri(baseUri, $"api/rep/token{(string.IsNullOrEmpty(userId) ? "" : $"?userId={Uri.EscapeDataString(userId)}")}");

    using var client = httpFactory.CreateClient("rep-proxy");
    using var req = new HttpRequestMessage(HttpMethod.Get, target);
    if (!string.IsNullOrEmpty(key)) req.Headers.Add("X-Rep-Key", key);

    using var resp = await client.SendAsync(req, ct);
    var json = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapPost("/rep/register", async (
    HttpContext http,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    CancellationToken ct) =>
{
    var (baseUrl, key) = RepProxyConfig(cfg);
    if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        return Results.Problem("Rep softphone backend is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    string body;
    using (var reader = new StreamReader(http.Request.Body))
        body = await reader.ReadToEndAsync(ct);

    var target = new Uri(baseUri, "api/rep/register");
    using var client = httpFactory.CreateClient("rep-proxy");
    using var req = new HttpRequestMessage(HttpMethod.Post, target)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
    if (!string.IsNullOrEmpty(key)) req.Headers.Add("X-Rep-Key", key);

    using var resp = await client.SendAsync(req, ct);
    var json = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

// Proxies POST /rep/hangup → API /api/rep/hangup so the browser can trigger a full
// forEveryone HangUp (terminates both the rep leg AND the PSTN customer leg) without
// holding the backend shared secret directly.
app.MapPost("/rep/hangup", async (
    HttpContext http,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    CancellationToken ct) =>
{
    var (baseUrl, key) = RepProxyConfig(cfg);
    if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        return Results.Problem("Rep softphone backend is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var target = new Uri(baseUri, "api/rep/hangup");
    using var client = httpFactory.CreateClient("rep-proxy");
    using var req = new HttpRequestMessage(HttpMethod.Post, target);
    if (!string.IsNullOrEmpty(key)) req.Headers.Add("X-Rep-Key", key);

    using var resp = await client.SendAsync(req, ct);
    var json = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

namespace CallCenterTranscription.Web
{
    public partial class Program;
}
