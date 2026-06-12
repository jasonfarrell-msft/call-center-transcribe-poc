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

apiRoutes.MapGet("/session/current", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetCurrentSession()));
apiRoutes.MapGet("/session/current-state", (PipelineCurrentStateStore currentStateStore) =>
    Results.Ok(currentStateStore.GetSnapshot()));

apiRoutes.MapGet("/mission-control/health", (
    IScriptedScenarioFeed scriptedScenarioFeed,
    IConfiguration configuration) =>
    Results.Ok(BuildMissionControlHealth(scriptedScenarioFeed, configuration)));

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

static MissionControlHealthResponse BuildMissionControlHealth(
    IScriptedScenarioFeed scriptedScenarioFeed,
    IConfiguration configuration)
{
    var baseline = scriptedScenarioFeed.GetMissionControlHealth();
    var reasoningMode = configuration.GetValue<string>("Reasoning:Mode") ?? "Mock";
    var normalizedReasoningMode = reasoningMode.Trim().ToLowerInvariant() switch
    {
        "live" => "live",
        "hybrid" => "hybrid",
        _ => "mock"
    };
    var foundryConfigured = !string.IsNullOrWhiteSpace(configuration["Reasoning:FoundryChatCompletionsUrl"]);
    var reasoningComponent = new MissionControlComponentHealth
    {
        ComponentId = "agent-assist-reasoning",
        DisplayName = "Agent Assist Reasoning",
        Status = normalizedReasoningMode switch
        {
            "live" when foundryConfigured => "healthy",
            "live" => "degraded",
            "hybrid" => "degraded",
            _ => "mock"
        },
        Readiness = normalizedReasoningMode,
        IsLive = normalizedReasoningMode == "live" && foundryConfigured,
        Evidence = normalizedReasoningMode switch
        {
            "live" when foundryConfigured => "Azure AI Foundry reasoning is live with grounding in Kira content pack.",
            "live" => "Live reasoning requested but Reasoning:FoundryChatCompletionsUrl is missing; fallback will be used.",
            "hybrid" => "Hybrid reasoning enabled: Azure AI Foundry primary with mock fallback for reliability.",
            _ => "Mock reasoning mode active for deterministic demo reliability."
        },
        LastCheckedUtc = DateTimeOffset.UtcNow
    };

    var baselineComponents = baseline.Components
        .Where(component => !string.Equals(component.ComponentId, reasoningComponent.ComponentId, StringComparison.OrdinalIgnoreCase))
        .Append(reasoningComponent)
        .ToArray();

    var liveAudioMode = string.Equals(
        configuration.GetValue<string>("AudioSource:Mode"),
        "Acs",
        StringComparison.OrdinalIgnoreCase);

    if (!liveAudioMode)
    {
        return baseline with
        {
            Components = baselineComponents
        };
    }

    var speechConfigured = !string.IsNullOrWhiteSpace(configuration["Speech:Endpoint"])
        && !string.IsNullOrWhiteSpace(configuration["Speech:Region"]);
    var translatorConfigured = !string.IsNullOrWhiteSpace(configuration["Translator:Endpoint"]);
    var acsConfigured = !string.IsNullOrWhiteSpace(configuration["Acs:Endpoint"]);
    var now = DateTimeOffset.UtcNow;

    var componentsById = baselineComponents.ToDictionary(component => component.ComponentId, StringComparer.OrdinalIgnoreCase);

    MissionControlComponentHealth Resolve(string componentId, Func<MissionControlComponentHealth, MissionControlComponentHealth> update)
    {
        if (!componentsById.TryGetValue(componentId, out var component))
        {
            component = new MissionControlComponentHealth
            {
                ComponentId = componentId,
                DisplayName = componentId,
                LastCheckedUtc = now
            };
        }

        return update(component with { LastCheckedUtc = now });
    }

    componentsById["mock-feed"] = Resolve("mock-feed", component => component with
    {
        Status = "deferred",
        Readiness = "standby",
        IsLive = false,
        Evidence = "Mock feed remains available as explicit fallback while live ACS/Speech is active."
    });

    componentsById["azure-ai-speech"] = Resolve("azure-ai-speech", component => component with
    {
        Status = speechConfigured ? "healthy" : "degraded",
        Readiness = speechConfigured ? "live" : "config-missing",
        IsLive = speechConfigured,
        Evidence = speechConfigured
            ? "Live Azure AI Speech transcription is enabled for ACS audio."
            : "Speech endpoint/region configuration missing; transcript falls back to mock mode."
    });

    componentsById["azure-ai-translator"] = Resolve("azure-ai-translator", component => component with
    {
        Status = translatorConfigured ? "healthy" : "degraded",
        Readiness = translatorConfigured ? "live" : "fallback-mock",
        IsLive = translatorConfigured,
        Evidence = translatorConfigured
            ? "Live Azure AI Translator is enabled for non-English utterances."
            : "Translator endpoint configuration missing; non-English turns remain untranslated."
    });

    componentsById["acs-media-routes"] = Resolve("acs-media-routes", component => component with
    {
        Status = acsConfigured ? "healthy" : "degraded",
        Readiness = acsConfigured ? "live" : "config-missing",
        IsLive = acsConfigured,
        Evidence = acsConfigured
            ? "ACS callbacks and media stream routes are configured for live ingress."
            : "ACS endpoint configuration missing; incoming call/media stream cannot activate."
    });

    var orderedComponents = baselineComponents
        .Select(component => componentsById.TryGetValue(component.ComponentId, out var updated) ? updated : component with { LastCheckedUtc = now })
        .ToArray();

    var reasoningLiveHealthy = normalizedReasoningMode != "live" || foundryConfigured;
    var allLiveDependenciesHealthy = speechConfigured && translatorConfigured && acsConfigured && reasoningLiveHealthy;
    var overallStatus = allLiveDependenciesHealthy ? "healthy" : "degraded";

    return baseline with
    {
        OverallStatus = overallStatus,
        GeneratedAtUtc = now,
        IsMockFeedActive = false,
        AcsMediaRoutesLiveReady = acsConfigured,
        Summary = allLiveDependenciesHealthy
            ? "Live ACS, Speech, and Translator pipeline is active."
            : "Live mode active with degraded dependencies. Mock fallback remains available.",
        Components = orderedComponents
    };
}

public partial class Program;
