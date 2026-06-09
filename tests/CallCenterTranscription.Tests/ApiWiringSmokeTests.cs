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

        Assert.IsType<AcsAudioSource>(audioSource);
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
    public async Task ApiHost_ExposesLiveCurrentSessionByDefault()
    {
        using var client = factory.CreateClient();

        var session = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");

        Assert.NotNull(session);
        Assert.Equal(string.Empty, session.Call.CallId);
        Assert.Equal(string.Empty, session.Call.CustomerName);
        Assert.Equal("waiting", session.Call.State);
        Assert.Equal("acs-live", session.Call.Source);
        Assert.False(session.IsMockFeedActive);
    }

    [Fact]
    public async Task ApiHost_ExposesEmptyTranscriptAndTranslationByDefault()
    {
        using var client = factory.CreateClient();

        var transcript = await client.GetFromJsonAsync<List<TranscriptEvent>>("/api/events/transcript");
        var translations = await client.GetFromJsonAsync<List<TranslationEvent>>("/api/events/translation");

        Assert.NotNull(transcript);
        Assert.NotNull(translations);
        Assert.Empty(transcript);
        Assert.Empty(translations);
    }

    [Fact]
    public async Task ApiHost_ExposesLiveSentimentWaitingStateByDefault()
    {
        using var client = factory.CreateClient();

        var sentiment = await client.GetFromJsonAsync<SentimentFeedResponse>("/api/events/sentiment");

        Assert.NotNull(sentiment);
        Assert.Equal(string.Empty, sentiment.CallId);
        Assert.Equal("neutral", sentiment.Summary.OverallLabel);
        Assert.Empty(sentiment.Events);
    }

    [Fact]
    public async Task ApiHost_ExposesEmptyDerivedEventsByDefault()
    {
        using var client = factory.CreateClient();

        var churnRisk = await client.GetFromJsonAsync<List<ChurnRiskEvent>>("/api/events/churn-risk");
        var knowledgeCards = await client.GetFromJsonAsync<List<KnowledgeCardEvent>>("/api/events/knowledge-cards");
        var nextBestActions = await client.GetFromJsonAsync<List<NextBestActionEvent>>("/api/events/next-best-action");

        Assert.NotNull(churnRisk);
        Assert.NotNull(knowledgeCards);
        Assert.NotNull(nextBestActions);
        Assert.Empty(churnRisk);
        Assert.Empty(knowledgeCards);
        Assert.Empty(nextBestActions);
    }

    [Fact]
    public async Task ApiHost_ExposesLiveCurrentStateWaitingPayloadByDefault()
    {
        using var client = factory.CreateClient();

        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");

        Assert.NotNull(currentState);
        Assert.Equal(string.Empty, currentState.Call.CallId);
        Assert.Equal("waiting", currentState.Call.State);
        Assert.Equal("acs-live", currentState.Call.Source);
        Assert.False(currentState.IsMockFeedActive);
        Assert.Equal("full_history_for_active_call", currentState.StreamReplayPolicy);
        Assert.Equal("neutral", currentState.SentimentSummary.OverallLabel);
        Assert.Empty(currentState.TranscriptEvents);
        Assert.Empty(currentState.TranslationEvents);
        Assert.Empty(currentState.SentimentEvents);
        Assert.Empty(currentState.ChurnRiskEvents);
        Assert.Empty(currentState.KnowledgeCardEvents);
        Assert.Empty(currentState.NextBestActionEvents);
    }

    [Fact]
    public async Task ApiHost_ExposesScriptedCurrentStateOnlyWhenMockModeExplicitlyConfigured()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Mock"
        });
        using var mockFactory = new WebApplicationFactory<Program>();
        using var client = mockFactory.CreateClient();

        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");

        Assert.NotNull(currentState);
        Assert.Equal("call-propane-retention-0001", currentState.Call.CallId);
        Assert.True(currentState.IsMockFeedActive);
        Assert.Equal(5, currentState.TranscriptEvents.Count);
        Assert.Single(currentState.TranslationEvents);
        Assert.Equal(3, currentState.SentimentEvents.Count);
    }

    [Fact]
    public async Task ApiHost_ExposesScriptedCurrentSessionOnlyWhenMockModeExplicitlyConfigured()
    {
        using var envScope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AudioSource__Mode"] = "Mock"
        });
        using var mockFactory = new WebApplicationFactory<Program>();
        using var client = mockFactory.CreateClient();

        var currentSession = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");

        Assert.NotNull(currentSession);
        Assert.Equal("call-propane-retention-0001", currentSession.Call.CallId);
        Assert.NotEmpty(currentSession.Call.CustomerName);
        Assert.True(currentSession.IsMockFeedActive);
    }

    [Fact]
    public async Task ApiHost_LiveCurrentStateAndSessionReuseStableStartAndReplayHistory()
    {
        using var isolatedFactory = factory.WithWebHostBuilder(_ => { });
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var activeCallStore = services.GetRequiredService<ActiveCallStore>();
            var currentStateStore = services.GetRequiredService<PipelineCurrentStateStore>();
            var liveSentimentStore = services.GetRequiredService<LiveSentimentStore>();
            var startedAt = DateTimeOffset.Parse("2026-06-09T18:00:00Z");

            activeCallStore.SetCallId("call-live-restore-1", startedAt);
            currentStateStore.ResetForCall("call-live-restore-1");
            liveSentimentStore.Reset("call-live-restore-1");

            currentStateStore.AppendTranscriptEvent(new TranscriptEvent
            {
                CallId = "call-live-restore-1",
                EventId = "evt-transcript-1",
                Sequence = 1,
                UtteranceId = "utt-1",
                Text = "I am frustrated with the billing jump.",
                IsFinal = true,
                SpeakerDisplayLabel = "Customer",
                SpeakerRole = "customer",
                SpeakerLabelSource = "test",
                DetectedLanguage = "en-US",
                Source = "test"
            });
            currentStateStore.AppendTranslationEvent(new TranslationEvent
            {
                CallId = "call-live-restore-1",
                EventId = "evt-translation-1",
                Sequence = 1,
                UtteranceId = "utt-1",
                RelatedTranscriptEventId = "evt-transcript-1",
                RelatedTranscriptSequence = 1,
                OriginalText = "I am frustrated with the billing jump.",
                TranslatedText = "I am frustrated with the billing jump.",
                SourceLanguage = "en",
                TargetLanguage = "en",
                Source = "test"
            });
            currentStateStore.AppendChurnRiskEvent(new ChurnRiskEvent
            {
                CallId = "call-live-restore-1",
                EventId = "evt-churn-1",
                Sequence = 1,
                UtteranceId = "utt-1",
                RelatedTranscriptEventId = "evt-transcript-1",
                RelatedTranscriptSequence = 1,
                RiskLevel = "high",
                RiskScore = 0.84,
                Rationale = "Customer mentioned billing jump.",
                Source = "test"
            });
            currentStateStore.AppendKnowledgeCardEvent(new KnowledgeCardEvent
            {
                CallId = "call-live-restore-1",
                EventId = "evt-card-1",
                Sequence = 1,
                UtteranceId = "utt-1",
                RelatedTranscriptEventId = "evt-transcript-1",
                RelatedTranscriptSequence = 1,
                Cards =
                [
                    new KnowledgeCard
                    {
                        Id = "card-1",
                        Title = "Billing recovery",
                        Snippet = "Offer budget billing and service credit."
                    }
                ],
                Source = "test"
            });
            currentStateStore.AppendNextBestActionEvent(new NextBestActionEvent
            {
                CallId = "call-live-restore-1",
                EventId = "evt-nba-1",
                Sequence = 1,
                UtteranceId = "utt-1",
                RelatedTranscriptEventId = "evt-transcript-1",
                RelatedTranscriptSequence = 1,
                Action = "Offer budget billing",
                Confidence = 0.88,
                Reasoning = "Matches billing-jump retention guidance.",
                Source = "test"
            });
            liveSentimentStore.Append("call-live-restore-1", "I am frustrated with the billing jump.");
        }

        using var client = isolatedFactory.CreateClient();

        var currentSession = await client.GetFromJsonAsync<SessionCurrentResponse>("/api/session/current");
        var currentState = await client.GetFromJsonAsync<PipelineCurrentStateResponse>("/api/session/current-state");

        Assert.NotNull(currentSession);
        Assert.NotNull(currentState);
        Assert.Equal("call-live-restore-1", currentSession.Call.CallId);
        Assert.Equal("call-live-restore-1", currentState.Call.CallId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-09T18:00:00Z"), currentSession.Call.StartedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-06-09T18:00:00Z"), currentState.Call.StartedAtUtc);
        Assert.Single(currentState.TranscriptEvents);
        Assert.Single(currentState.TranslationEvents);
        Assert.Single(currentState.ChurnRiskEvents);
        Assert.Single(currentState.KnowledgeCardEvents);
        Assert.Single(currentState.NextBestActionEvents);
    }

    [Fact]
    public async Task ApiHost_ExposesLiveInteractionMissionControlDefaults()
    {
        using var client = factory.CreateClient();

        var missionControl = await client.GetFromJsonAsync<MissionControlHealthResponse>("/api/mission-control/health");

        Assert.NotNull(missionControl);
        Assert.Equal("degraded", missionControl.OverallStatus);
        Assert.False(missionControl.IsMockFeedActive);
        Assert.False(missionControl.AcsMediaRoutesLiveReady);
        Assert.Contains("Live mode active", missionControl.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "mock-feed" && component.Status == "deferred");
        Assert.Contains(missionControl.Components, component =>
            component.ComponentId == "acs-media-routes" && component.Status == "degraded");
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
            ["Security__Auth__Audience"] = "api://call-center-transcription-api"
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
            ["Security__Auth__Audience"] = "api://call-center-transcription-api"
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
            ["Security__Auth__Audience"] = ""
        });
        using var invalidFactory = new WebApplicationFactory<Program>();

        var exception = Assert.Throws<InvalidOperationException>(() => invalidFactory.CreateClient());
        Assert.Contains("Security:RequireAuth=true", exception.Message, StringComparison.Ordinal);
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
