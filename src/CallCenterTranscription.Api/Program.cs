using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Hubs;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
var requireAuth = builder.Configuration.GetValue<bool>("Security:RequireAuth");
var authAuthority = builder.Configuration["Security:Auth:Authority"];
var authAudience = builder.Configuration["Security:Auth:Audience"];

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

// CORS for the browser SignalR client (separate Web origin → API origin). Specific origins +
// AllowCredentials is REQUIRED for the SignalR negotiate handshake; AllowAnyOrigin is invalid
// with credentials, so origins come from config (Cors:AllowedOrigins, e.g. the web app URL).
const string consoleCorsPolicy = "ConsoleCors";
var corsAllowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(consoleCorsPolicy, policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});
builder.Services.AddCallCenterServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseWebSockets(); // Required before route execution for WebSocket upgrade support.
app.UseRouting();
app.UseCors(consoleCorsPolicy); // After routing, before auth (reviewer fix). Covers /hubs negotiate + /api/calls/active.
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
apiRoutes.MapGet("/session/current-state", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot()));

apiRoutes.MapGet("/mission-control/health", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetMissionControlHealth()));

var eventRoutes = apiRoutes.MapGroup("/events");

// In ACS (live) mode the sentiment panel is driven by the live transcript rather than the
// scripted demo feed, so the meter moves with the real conversation.
var liveSentimentMode = string.Equals(
    app.Configuration.GetValue<string>("AudioSource:Mode"), "Acs", StringComparison.OrdinalIgnoreCase);

eventRoutes.MapGet("/transcript", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot().TranscriptEvents));
eventRoutes.MapGet("/translation", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot().TranslationEvents));
eventRoutes.MapGet("/sentiment", (
    PipelineCurrentStateStore currentStateStore,
    LiveSentimentStore liveSentiment) =>
{
    var snapshot = currentStateStore.GetSnapshot();

    // Live (Acs) mode: serve the rolling, transcript-driven sentiment so the meter tracks the
    // real call. Mock mode: keep the scripted demo feed.
    return Results.Ok(liveSentimentMode
        ? liveSentiment.GetFeed()
        : new SentimentFeedResponse
        {
            CallId = snapshot.Call.CallId,
            Summary = snapshot.SentimentSummary,
            Events = snapshot.SentimentEvents
        });
});
eventRoutes.MapGet("/churn-risk", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot().ChurnRiskEvents));
eventRoutes.MapGet("/knowledge-cards", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot().KnowledgeCardEvents));
eventRoutes.MapGet("/next-best-action", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot().NextBestActionEvents));

var pipelineHub = app.MapHub<PipelineHub>("/hubs/pipeline");
if (requireAuth)
{
    pipelineHub.RequireAuthorization("AgentAssistAccess");
}

// ACS call-path routes: IncomingCall webhook + media-stream WebSocket.
// Always mapped; dormant when AudioSource:Mode=Mock (no calls answered in mock mode).
app.MapAcsRoutes();

// Rep softphone control-plane: VoIP token + register/AddParticipant.
app.MapRepRoutes();

app.Run();

public partial class Program;
