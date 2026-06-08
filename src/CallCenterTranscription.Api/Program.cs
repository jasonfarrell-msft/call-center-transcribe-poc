using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Hubs;
using CallCenterTranscription.Shared.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAuthentication();
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

var requireAuth = builder.Configuration.GetValue<bool>("Security:RequireAuth");
var apiRoutes = app.MapGroup("/api");
if (requireAuth)
{
    apiRoutes.RequireAuthorization("AgentAssistAccess");
}

apiRoutes.MapGet("/session/current", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetCurrentSession()));
apiRoutes.MapGet("/session/current-state", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetCurrentState()));

apiRoutes.MapGet("/mission-control/health", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetMissionControlHealth()));

var eventRoutes = apiRoutes.MapGroup("/events");
eventRoutes.MapGet("/transcript", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetTranscriptEvents()));
eventRoutes.MapGet("/translation", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetTranslationEvents()));
eventRoutes.MapGet("/sentiment", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetSentimentFeed()));
eventRoutes.MapGet("/churn-risk", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetChurnRiskEvents()));
eventRoutes.MapGet("/knowledge-cards", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetKnowledgeCardEvents()));
eventRoutes.MapGet("/next-best-action", (IScriptedScenarioFeed scriptedScenarioFeed) =>
    Results.Ok(scriptedScenarioFeed.GetNextBestActionEvents()));

var pipelineHub = app.MapHub<PipelineHub>("/hubs/pipeline");
if (requireAuth)
{
    pipelineHub.RequireAuthorization("AgentAssistAccess");
}

app.Run();

public partial class Program;
