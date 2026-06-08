using CallCenterTranscription.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.Configure<BackendApiOptions>(builder.Configuration.GetSection(BackendApiOptions.SectionName));
builder.Services.AddHttpClient<PipelineApiClient>();

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

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

namespace CallCenterTranscription.Web
{
    public partial class Program;
}
