using System.Net;
using System.Net.Http.Json;
using CallCenterTranscription.Ai;
using CallCenterTranscription.Api;
using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Telephony;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenterTranscription.Tests;

public sealed class ApiWiringSmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void ApiHost_RegistersExpectedRoutesAndServices()
    {
        using var scope = factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        var audioSource = serviceProvider.GetRequiredService<IAudioSource>();
        var reasoningClient = serviceProvider.GetRequiredService<IReasoningClient>();
        var routePatterns = serviceProvider
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsType<MockAudioSource>(audioSource);
        Assert.IsType<MockReasoningClient>(reasoningClient);
        Assert.Contains("/healthz", routePatterns);
        Assert.Contains("/api/session/current", routePatterns);
        Assert.Contains("/api/session/current-state", routePatterns);
        Assert.Contains("/api/mission-control/health", routePatterns);
        Assert.Contains("/api/events/transcript", routePatterns);
        Assert.Contains("/api/events/translation", routePatterns);
        Assert.Contains("/api/events/sentiment", routePatterns);
        Assert.Contains("/api/events/churn-risk", routePatterns);
        Assert.Contains("/api/events/knowledge-cards", routePatterns);
        Assert.Contains("/api/events/next-best-action", routePatterns);
        Assert.Contains("/hubs/pipeline", routePatterns);
        Assert.Contains("/hubs/pipeline/negotiate", routePatterns);
    }

    [Fact]
    public async Task ApiHost_ExposesHealthEndpoint()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/healthz");

        Assert.True(
            response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect,
            $"Expected success or HTTPS redirect from /healthz, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task ApiHost_ExposesCurrentSessionScenarioMetadata()
    {
        using var client = factory.CreateClient();

        var session = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");

        Assert.NotNull(session);
        Assert.Equal("call-propane-retention-0001", session.Call.CallId);
        Assert.Equal("Maria Alvarez", session.Call.CustomerName);
        Assert.True(session.IsMockFeedActive);
        Assert.Equal("cooling_down", session.SentimentSummary.OverallLabel);
    }

    [Fact]
    public async Task ApiHost_ExposesCorrelatedTranscriptAndTranslationEvents()
    {
        using var client = factory.CreateClient();

        var transcript = await client.GetFromJsonAsync<List<TranscriptEvent>>("/api/events/transcript");
        var translations = await client.GetFromJsonAsync<List<TranslationEvent>>("/api/events/translation");

        Assert.NotNull(transcript);
        Assert.NotNull(translations);
        Assert.NotEmpty(transcript);
        Assert.Single(translations);
        Assert.Contains(translations[0].UtteranceId, transcript.Select(t => t.UtteranceId));
        Assert.Equal("evt-transcript-0003", translations[0].RelatedTranscriptEventId);
        Assert.Equal("es", translations[0].SourceLanguage);
    }

    [Fact]
    public async Task ApiHost_ExposesSentimentFeedSummary()
    {
        using var client = factory.CreateClient();

        var sentiment = await client.GetFromJsonAsync<SentimentFeedResponse>("/api/events/sentiment");

        Assert.NotNull(sentiment);
        Assert.Equal("call-propane-retention-0001", sentiment.CallId);
        Assert.Equal("improving", sentiment.Summary.Trend);
        Assert.Equal(3, sentiment.Events.Count);
    }

    [Fact]
    public async Task ApiHost_ExposesChurnKnowledgeAndNextBestActionEvents()
    {
        using var client = factory.CreateClient();

        var churnRisk = await client.GetFromJsonAsync<List<ChurnRiskEvent>>("/api/events/churn-risk");
        var knowledgeCards = await client.GetFromJsonAsync<List<KnowledgeCardEvent>>("/api/events/knowledge-cards");
        var nextBestActions = await client.GetFromJsonAsync<List<NextBestActionEvent>>("/api/events/next-best-action");

        Assert.NotNull(churnRisk);
        Assert.NotNull(knowledgeCards);
        Assert.NotNull(nextBestActions);
        Assert.NotEmpty(churnRisk);
        Assert.NotEmpty(knowledgeCards);
        Assert.NotEmpty(nextBestActions);
        Assert.All(churnRisk, item => Assert.Equal("call-propane-retention-0001", item.CallId));
        Assert.All(knowledgeCards, item => Assert.Equal("call-propane-retention-0001", item.CallId));
        Assert.All(nextBestActions, item => Assert.Equal("call-propane-retention-0001", item.CallId));
        Assert.Equal("evt-transcript-0003", churnRisk[0].RelatedTranscriptEventId);
        Assert.Equal("evt-transcript-0003", knowledgeCards[0].RelatedTranscriptEventId);
        Assert.Equal("evt-transcript-0003", nextBestActions[0].RelatedTranscriptEventId);
    }

    [Fact]
    public async Task ApiHost_ExposesCurrentStateReplayPayload()
    {
        using var client = factory.CreateClient();

        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");

        Assert.NotNull(currentState);
        Assert.Equal("call-propane-retention-0001", currentState.Call.CallId);
        Assert.Equal("full_history_for_active_call", currentState.StreamReplayPolicy);
        Assert.Equal(5, currentState.TranscriptEvents.Count);
        Assert.Single(currentState.TranslationEvents);
        Assert.Equal(3, currentState.SentimentEvents.Count);
        Assert.NotEmpty(currentState.ChurnRiskEvents);
        Assert.NotEmpty(currentState.KnowledgeCardEvents);
        Assert.NotEmpty(currentState.NextBestActionEvents);
    }

    [Fact]
    public async Task ApiHost_ExposesMissionControlMockAndDeferredStates()
    {
        using var client = factory.CreateClient();

        var missionControl = await client.GetFromJsonAsync<MissionControlHealthResponse>("/api/mission-control/health");

        Assert.NotNull(missionControl);
        Assert.Equal("degraded", missionControl.OverallStatus);
        Assert.True(missionControl.IsMockFeedActive);
        Assert.False(missionControl.AcsMediaRoutesLiveReady);
        Assert.Contains("not live-ready", missionControl.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "mock-feed" && component.Status == "mock");
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "acs-media-routes" && component.Status == "deferred");
    }

    [Fact]
    public async Task ApiHost_RequiresAuthenticationForSessionRoute_WhenRequireAuthEnabled()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["Security__RequireAuth"] = "true",
            ["Security__Auth__Authority"] = "https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0",
            ["Security__Auth__Audience"] = "api://call-center-transcription-api",
            ["DemoSafety__DataMode"] = "Mock"
        });
        using var authFactory = new WebApplicationFactory<Program>();
        using var client = authFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/session/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiHost_RequiresAuthenticationForPipelineNegotiate_WhenRequireAuthEnabled()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["Security__RequireAuth"] = "true",
            ["Security__Auth__Authority"] = "https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0",
            ["Security__Auth__Audience"] = "api://call-center-transcription-api",
            ["DemoSafety__DataMode"] = "Mock"
        });
        using var authFactory = new WebApplicationFactory<Program>();
        using var client = authFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var content = new StringContent(string.Empty);
        var response = await client.PostAsync("/hubs/pipeline/negotiate?negotiateVersion=1", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ApiHost_RequireAuthWithoutAuthorityAndAudience_FailsFast()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["Security__RequireAuth"] = "true",
            ["Security__Auth__Authority"] = "",
            ["Security__Auth__Audience"] = "",
            ["DemoSafety__DataMode"] = "Mock"
        });
        using var invalidFactory = new WebApplicationFactory<Program>();

        var exception = Assert.Throws<InvalidOperationException>(() => invalidFactory.CreateClient());
        Assert.Contains("Security:RequireAuth=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiHost_NonMockDataMode_DoesNotThrow()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DemoSafety__DataMode"] = "Live"
        });
        using var invalidFactory = new WebApplicationFactory<Program>();

        using var client = invalidFactory.CreateClient();
        Assert.NotNull(client);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> originalValues;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            originalValues = values
                .ToDictionary(static pair => pair.Key, static pair => Environment.GetEnvironmentVariable(pair.Key));

            foreach (var (key, value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in originalValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
