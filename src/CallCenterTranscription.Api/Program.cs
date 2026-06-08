using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Hubs;
using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
var requireAuth = builder.Configuration.GetValue<bool>("Security:RequireAuth");
var authAuthority = builder.Configuration["Security:Auth:Authority"];
var authAudience = builder.Configuration["Security:Auth:Audience"];
var dataMode = builder.Configuration.GetValue<string>("DemoSafety:DataMode") ?? "Mock";

if (!string.Equals(dataMode, "Mock", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"DemoSafety:DataMode '{dataMode}' is not supported by the scripted feed. Keep DemoSafety:DataMode=Mock until live providers are implemented.");
}

if (requireAuth &&
    (string.IsNullOrWhiteSpace(authAuthority) || string.IsNullOrWhiteSpace(authAudience)))
{
    throw new InvalidOperationException(
        "Security:RequireAuth=true requires Security:Auth:Authority and Security:Auth:Audience to be configured.");
}

builder.Services.AddOpenApi();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrWhiteSpace(authAuthority))
        {
            options.Authority = authAuthority;
        }

        if (!string.IsNullOrWhiteSpace(authAudience))
        {
            options.Audience = authAudience;
            options.TokenValidationParameters.ValidateAudience = true;
        }

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    path.StartsWithSegments("/hubs/pipeline"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AgentAssistAccess", policy => policy.RequireAuthenticatedUser());
});
builder.Services.AddCallCenterServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

var apiRoutes = app.MapGroup("/api");
if (requireAuth)
{
    apiRoutes.RequireAuthorization("AgentAssistAccess");
}

apiRoutes.MapGet("/session/current", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetCurrentSession()));

apiRoutes.MapGet("/mission-control/health", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetMissionControlHealth()));

var eventRoutes = apiRoutes.MapGroup("/events");
eventRoutes.MapGet("/transcript", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetTranscriptEvents()));
eventRoutes.MapGet("/translation", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetTranslationEvents()));
eventRoutes.MapGet("/sentiment", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetSentimentFeed()));
eventRoutes.MapGet("/churn-risk", () => Results.Ok(Array.Empty<ChurnRiskEvent>()));
eventRoutes.MapGet("/knowledge-cards", () => Results.Ok(Array.Empty<KnowledgeCardEvent>()));
eventRoutes.MapGet("/next-best-action", () => Results.Ok(Array.Empty<NextBestActionEvent>()));

var pipelineHub = app.MapHub<PipelineHub>("/hubs/pipeline");
if (requireAuth)
{
    pipelineHub.RequireAuthorization("AgentAssistAccess");
}

app.Run();

public partial class Program;
