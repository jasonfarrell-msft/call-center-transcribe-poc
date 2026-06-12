using System.Net;
using System.Net.Http.Json;
using CallCenterTranscription.Ai;
using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Services;
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
        Assert.IsType<ConfiguredReasoningClient>(reasoningClient);
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
        Assert.Equal("Elena Morales", session.Call.CustomerName);
        Assert.True(session.IsMockFeedActive);
        Assert.Equal("resolved", session.SentimentSummary.OverallLabel);
        Assert.Contains("DemoScript__ScriptId", session.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiHost_LiveMode_ExposesPendingCallStateBeforeAnyTranscript()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Acs"
        });
        using var liveFactory = new WebApplicationFactory<Program>();

        var startedAtUtc = DateTimeOffset.Parse("2026-06-12T14:00:00Z");
        using (var scope = liveFactory.Services.CreateScope())
        {
            var currentStateStore = scope.ServiceProvider.GetRequiredService<PipelineCurrentStateStore>();
            currentStateStore.MarkPending("call-live-pending-1", startedAtUtc);
        }

        using var client = liveFactory.CreateClient();
        var session = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");
        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");
        var activeCall = await client.GetFromJsonAsync<ActiveCallStateResponse>("/api/calls/active");

        Assert.NotNull(session);
        Assert.NotNull(currentState);
        Assert.NotNull(activeCall);
        Assert.False(session.IsMockFeedActive);
        Assert.Equal("call-live-pending-1", session.Call.CallId);
        Assert.Equal("pending", session.Call.State);
        Assert.Equal("call-live-pending-1", currentState.Call.CallId);
        Assert.Equal("pending", currentState.Call.State);
        Assert.Empty(currentState.TranscriptEvents);
        Assert.Equal("live_lifecycle_only", currentState.StreamReplayPolicy);
        Assert.True(activeCall.AcceptAvailable);
        Assert.False(activeCall.RepAccepted);
        Assert.Equal("pending", activeCall.State);
        Assert.Equal(startedAtUtc, activeCall.StartedAtUtc);
    }

    [Fact]
    public async Task ApiHost_LiveMode_ExposesAcceptedCallStateWithoutReopeningAccept()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Acs"
        });
        using var liveFactory = new WebApplicationFactory<Program>();

        using (var scope = liveFactory.Services.CreateScope())
        {
            var currentStateStore = scope.ServiceProvider.GetRequiredService<PipelineCurrentStateStore>();
            currentStateStore.MarkPending("call-live-accepted-1", DateTimeOffset.Parse("2026-06-12T14:05:00Z"));
            currentStateStore.MarkAccepted("call-live-accepted-1");
        }

        using var client = liveFactory.CreateClient();
        var session = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");
        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");
        var activeCall = await client.GetFromJsonAsync<ActiveCallStateResponse>("/api/calls/active");

        Assert.NotNull(session);
        Assert.NotNull(currentState);
        Assert.NotNull(activeCall);
        Assert.Equal("accepted", session.Call.State);
        Assert.Equal("accepted", currentState.Call.State);
        Assert.Empty(currentState.TranscriptEvents);
        Assert.False(activeCall.AcceptAvailable);
        Assert.True(activeCall.RepAccepted);
        Assert.Equal("accepted", activeCall.State);
        Assert.Contains("Rep has accepted", session.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiHost_MockMode_IgnoresIncomingCallWebhookEvenWhenAcsEndpointExists()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Mock",
            ["Acs__Endpoint"] = "https://contoso.communication.azure.com"
        });
        using var mockFactory = new WebApplicationFactory<Program>();
        using var client = mockFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events/acs/incoming-call", new[]
        {
            new
            {
                eventType = "Microsoft.Communication.IncomingCall",
                data = new
                {
                    incomingCallContext = "synthetic-incoming-context"
                }
            }
        });

        response.EnsureSuccessStatusCode();

        var session = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");
        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");

        Assert.NotNull(session);
        Assert.NotNull(currentState);
        Assert.True(session.IsMockFeedActive);
        Assert.Equal("call-propane-retention-0001", session.Call.CallId);
        Assert.Equal("call-propane-retention-0001", currentState.Call.CallId);
        Assert.Equal("active", currentState.Call.State);
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
        Assert.Equal("es-US", translations[0].SourceLanguage);
    }

    [Fact]
    public async Task ApiHost_ExposesSentimentFeedSummary()
    {
        using var client = factory.CreateClient();

        var sentiment = await client.GetFromJsonAsync<SentimentFeedResponse>("/api/events/sentiment");

        Assert.NotNull(sentiment);
        Assert.Equal("call-propane-retention-0001", sentiment.CallId);
        Assert.Equal("resolved", sentiment.Summary.OverallLabel);
        Assert.Equal("improving", sentiment.Summary.Trend);
        Assert.True(sentiment.Events.Count >= 3);
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
        Assert.Equal("evt-transcript-0002", churnRisk[0].RelatedTranscriptEventId);
        Assert.Equal("evt-transcript-0002", knowledgeCards[0].RelatedTranscriptEventId);
        Assert.Equal("evt-transcript-0002", nextBestActions[0].RelatedTranscriptEventId);
        Assert.NotEmpty(knowledgeCards[0].Cards[0].MatchedEvidence);
        Assert.False(string.IsNullOrWhiteSpace(knowledgeCards[0].Cards[0].CitationLabel));
    }

    [Fact]
    public async Task ApiHost_ExposesCurrentStateReplayPayload()
    {
        using var client = factory.CreateClient();

        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");

        Assert.NotNull(currentState);
        Assert.Equal("call-propane-retention-0001", currentState.Call.CallId);
        Assert.Equal("full_history_for_active_call", currentState.StreamReplayPolicy);
        Assert.Equal(6, currentState.TranscriptEvents.Count);
        Assert.Single(currentState.TranslationEvents);
        Assert.True(currentState.SentimentEvents.Count >= 3);
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
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "agent-assist-reasoning"
            && component.Readiness == "mock"
            && component.Status == "mock");
    }

    [Fact]
    public async Task ApiHost_MissionControlReportsLiveSpeechTranslator_WhenAcsModeConfigured()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Acs",
            ["Acs__Endpoint"] = "https://contoso.communication.azure.com",
            ["Speech__Endpoint"] = "https://contoso-speech.cognitiveservices.azure.com",
            ["Speech__Region"] = "swedencentral",
            ["Translator__Endpoint"] = "https://api.cognitive.microsofttranslator.com"
        });
        using var liveFactory = new WebApplicationFactory<Program>();
        using var client = liveFactory.CreateClient();

        var missionControl = await client.GetFromJsonAsync<MissionControlHealthResponse>("/api/mission-control/health");

        Assert.NotNull(missionControl);
        Assert.False(missionControl.IsMockFeedActive);
        Assert.True(missionControl.AcsMediaRoutesLiveReady);
        Assert.Contains("Live ACS", missionControl.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "azure-ai-speech" && component.Status == "healthy" && component.IsLive);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "azure-ai-translator" && component.Status == "healthy" && component.IsLive);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "acs-media-routes" && component.Status == "healthy" && component.IsLive);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "agent-assist-reasoning"
            && component.Readiness == "mock"
            && component.Status == "mock");
    }

    [Fact]
    public async Task ApiHost_MissionControlShowsTranslatorFallback_WhenTranslatorNotConfigured()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Acs",
            ["Acs__Endpoint"] = "https://contoso.communication.azure.com",
            ["Speech__Endpoint"] = "https://contoso-speech.cognitiveservices.azure.com",
            ["Speech__Region"] = "swedencentral",
            ["Translator__Endpoint"] = string.Empty
        });
        using var liveFactory = new WebApplicationFactory<Program>();
        using var client = liveFactory.CreateClient();

        var missionControl = await client.GetFromJsonAsync<MissionControlHealthResponse>("/api/mission-control/health");

        Assert.NotNull(missionControl);
        Assert.False(missionControl.IsMockFeedActive);
        Assert.Contains("fallback", missionControl.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "azure-ai-translator"
            && component.Status == "degraded"
            && !component.IsLive
            && component.Readiness == "fallback-mock");
    }

    [Fact]
    public async Task ApiHost_MissionControlShowsHybridReasoningMode_WhenConfigured()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["Reasoning__Mode"] = "Hybrid",
            ["Reasoning__FoundryChatCompletionsUrl"] = "https://contoso.services.ai.azure.com/models/chat/completions"
        });
        using var hybridFactory = new WebApplicationFactory<Program>();
        using var client = hybridFactory.CreateClient();

        var missionControl = await client.GetFromJsonAsync<MissionControlHealthResponse>("/api/mission-control/health");

        Assert.NotNull(missionControl);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "agent-assist-reasoning"
            && component.Readiness == "hybrid"
            && component.Status == "degraded"
            && !component.IsLive);
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
