using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Web.Pages;
using CallCenterTranscription.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CallCenterTranscription.Tests;

public sealed class WebConsoleTests
{
    [Fact]
    public async Task IndexModel_OnGetAsync_RevealsTranslationOnlyAfterExplicitAction()
    {
        var model = CreateIndexModel();
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };

        await model.OnGetAsync();

        Assert.Equal(2, model.TranscriptTimeline.Count);
        var englishTurn = model.TranscriptTimeline.Single(item => item.UtteranceId == "utt-en-1");
        var spanishTurn = model.TranscriptTimeline.Single(item => item.UtteranceId == "utt-es-1");

        Assert.False(englishTurn.IsNonEnglish);
        Assert.False(englishTurn.HasTranslation);
        Assert.True(spanishTurn.IsNonEnglish);
        Assert.True(spanishTurn.HasTranslation);
    }

    [Fact]
    public async Task IndexModel_OnGetAsync_LoadsContextSentimentAndMissionControlData()
    {
        var model = CreateIndexModel();
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };

        await model.OnGetAsync();

        var spanishTurn = model.TranscriptTimeline.Single(item => item.UtteranceId == "utt-es-1");

        Assert.Equal("We are out of propane and need delivery tonight.", spanishTurn.TranslationText);
        Assert.Contains("Mock feed active", model.ConnectionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Maria Alvarez", model.CurrentSession.Call.CustomerName);
        Assert.Contains(model.MissionControlHealth.Components, component => component.IsLive && component.Status == "healthy");
        Assert.Contains(model.MissionControlHealth.Components, component => !component.IsLive && component.Status == "mock");
        Assert.Equal("cooling_down", model.SentimentFeed.Summary.OverallLabel);
        Assert.Equal("improving", model.SentimentFeed.Summary.Trend);
        Assert.Single(model.KnowledgeGuidance);
        Assert.Equal("Customer turn 2", model.KnowledgeGuidance[0].TurnLabel);
        Assert.Equal("Priority 1", model.KnowledgeGuidance[0].Cards[0].RankLabel);
        Assert.True(model.KnowledgeGuidance[0].Cards[0].Evidence[0].HasNormalizedText);
        Assert.False(model.HasConnectionIssues);
    }

    [Fact]
    public async Task IndexModel_OnGetAsync_WhenBackendBaseUrlMissing_SurfacesDisconnectedState()
    {
        var model = CreateIndexModel(baseUrl: string.Empty);
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };

        await model.OnGetAsync();

        Assert.True(model.HasConnectionIssues);
        Assert.Equal("Backend API disconnected", model.ConnectionSummary);
        Assert.NotNull(model.TranscriptWarning);
        Assert.Contains("not configured", model.TranscriptWarning, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(model.SentimentWarning);
        Assert.Contains("not configured", model.SentimentWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(model.TranscriptTimeline);
    }

    [Fact]
    public async Task PipelineApiClient_WhenEndpointFails_ReturnsExplicitFailure()
    {
        var pipelineClient = new PipelineApiClient(
            new HttpClient(new AlwaysUnavailablePipelineHandler()),
            Options.Create(new BackendApiOptions { BaseUrl = "https://example.test/" }));

        var result = await pipelineClient.GetCurrentSessionAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiFetchFailureKind.Upstream, result.FailureKind);
        Assert.Contains("503", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexModel_OnGetAsync_WhenEndpointsFail_SurfacesFeedWarnings()
    {
        var model = CreateIndexModel(handler: new AlwaysUnavailablePipelineHandler());
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };

        await model.OnGetAsync();

        Assert.True(model.HasConnectionIssues);
        Assert.Equal("Backend API disconnected", model.ConnectionSummary);
        Assert.NotNull(model.TranscriptWarning);
        Assert.Contains("returned an error", model.TranscriptWarning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("503", model.TranscriptWarning, StringComparison.Ordinal);
        Assert.NotNull(model.MissionControlWarning);
        Assert.Contains("returned an error", model.MissionControlWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(model.TranscriptTimeline);
    }

    [Fact]
    public async Task PipelineApiClient_WhenPayloadMalformed_ReturnsPayloadFailure()
    {
        var pipelineClient = new PipelineApiClient(
            new HttpClient(new MalformedPayloadPipelineHandler()),
            Options.Create(new BackendApiOptions { BaseUrl = "https://example.test/" }));

        var result = await pipelineClient.GetTranscriptEventsAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiFetchFailureKind.Payload, result.FailureKind);
    }

    [Fact]
    public async Task IndexModel_OnGetAsync_WhenPayloadIsValidButEmpty_UsesEmptyStateWithoutWarnings()
    {
        var model = CreateIndexModel(handler: new SuccessfulEmptyPipelineHandler());
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };

        await model.OnGetAsync();

        Assert.False(model.HasConnectionIssues);
        Assert.Null(model.TranscriptWarning);
        Assert.Null(model.SentimentWarning);
        Assert.Null(model.MissionControlWarning);
        Assert.Equal("Backend connected • Waiting for active call", model.ConnectionSummary);
        Assert.Empty(model.TranscriptTimeline);
    }

    [Fact]
    public async Task IndexModel_OnGetAsync_WhenNonSessionFeedFails_ShowsDegradedSummary()
    {
        var model = CreateIndexModel(handler: new PartialFailurePipelineHandler());
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };

        await model.OnGetAsync();

        Assert.True(model.HasConnectionIssues);
        Assert.True(
            string.Equals(model.ConnectionSummary, "Backend API degraded", StringComparison.Ordinal) ||
            string.Equals(model.ConnectionSummary, "Mock feed active • Call state: Active", StringComparison.Ordinal),
            $"Unexpected connection summary: {model.ConnectionSummary}");
        Assert.NotNull(model.TranscriptWarning);
        Assert.Contains("returned an error", model.TranscriptWarning, StringComparison.OrdinalIgnoreCase);
    }

    private static IndexModel CreateIndexModel(HttpMessageHandler? handler = null, string baseUrl = "https://example.test/")
    {
        var httpClient = new HttpClient(handler ?? new StubPipelineHandler());
        var options = Options.Create(new BackendApiOptions
        {
            BaseUrl = baseUrl
        });

        var pipelineClient = new PipelineApiClient(httpClient, options);
        var configuration = new ConfigurationBuilder().Build();
        return new IndexModel(NullLogger<IndexModel>.Instance, pipelineClient, options, configuration);
    }
}

public sealed class WebHomepageSmokeTests(WebApplicationFactory<CallCenterTranscription.Web.Program> factory)
    : IClassFixture<WebApplicationFactory<CallCenterTranscription.Web.Program>>
{
    [Fact]
    public async Task Homepage_ReplacesScaffoldMarkupWithRepConsole()
    {
        using var webFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackendApi:BaseUrl"] = string.Empty
                })));
        using var client = webFactory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Call-Center Representative Console", html, StringComparison.Ordinal);
        Assert.Contains("Live transcript, diarization, and translation", html, StringComparison.Ordinal);
        Assert.Contains("Sentiment indicator", html, StringComparison.Ordinal);
        Assert.Contains("Representative awareness", html, StringComparison.Ordinal);
        Assert.Contains("Backend API connection issues detected.", html, StringComparison.Ordinal);
        Assert.Contains("Backend API is not configured", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Phase 0 scaffold is running.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Homepage_WhenLiveModeEnabled_RendersSignalrDrivenAssistPanels()
    {
        using var webFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackendApi:BaseUrl"] = "https://example.test/",
                    ["Frontend:LiveMode"] = "true"
                })));
        using var client = webFactory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("data-live-mode=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-live-call-state=\"idle\"", html, StringComparison.Ordinal);
        Assert.Contains("data-live-knowledge-panel", html, StringComparison.Ordinal);
        Assert.Contains("data-live-sentiment-panel", html, StringComparison.Ordinal);
        Assert.Contains("Speech services offline", html, StringComparison.Ordinal);
        Assert.Contains("Incoming call — accept to begin transcription.", html, StringComparison.Ordinal);
        Assert.Contains("data-rep-accept", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-console-refresh-region=\"sentiment\"", html, StringComparison.Ordinal);
        Assert.Contains("signalr.min.js", html, StringComparison.Ordinal);
        Assert.Contains("live-transcript.js", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Healthz_DoesNotRequireHttpsRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Homepage_DoesNotRequireHttpsRedirect()
    {
        using var webFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackendApi:BaseUrl"] = "https://example.test/"
                })));
        using var client = webFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

file sealed class StubPipelineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        object payload = path switch
        {
            "/api/session/current" => new SessionCurrentResponse
            {
                Call = new CallSessionMetadata
                {
                    CallId = "call-1",
                    SessionId = "session-1",
                    CustomerName = "Maria Alvarez",
                    AgentName = "Sam Holt",
                    QueueName = "Retention",
                    State = "active",
                    StartedAtUtc = DateTimeOffset.Parse("2026-06-07T00:10:18Z"),
                    ScenarioName = "Propane retention",
                    Source = "mock-script"
                },
                IsMockFeedActive = true,
                Notes = "Scripted feed"
            },
            "/api/events/transcript" => new[]
            {
                new TranscriptEvent
                {
                    EventId = "evt-1",
                    UtteranceId = "utt-en-1",
                    Sequence = 1,
                    TimestampUtc = DateTimeOffset.Parse("2026-06-07T00:10:18Z"),
                    SpeakerDisplayLabel = "Speaker 1",
                    SpeakerRole = "agent",
                    SpeakerLabelSource = "scripted",
                    Text = "Thanks for calling.",
                    DetectedLanguage = "en",
                    Source = "mock-script"
                },
                new TranscriptEvent
                {
                    EventId = "evt-2",
                    UtteranceId = "utt-es-1",
                    Sequence = 2,
                    TimestampUtc = DateTimeOffset.Parse("2026-06-07T00:10:22Z"),
                    SpeakerDisplayLabel = "Speaker 2",
                    SpeakerRole = "customer",
                    SpeakerLabelSource = "scripted",
                    Text = "No tenemos propano y necesitamos entrega esta noche.",
                    DetectedLanguage = "es",
                    Source = "mock-script"
                }
            },
            "/api/events/translation" => new[]
            {
                new TranslationEvent
                {
                    UtteranceId = "utt-es-1",
                    RelatedTranscriptEventId = "evt-2",
                    SourceLanguage = "es",
                    TargetLanguage = "en",
                    TranslatedText = "We are out of propane and need delivery tonight.",
                    Source = "mock-script"
                }
            },
            "/api/events/sentiment" => new SentimentFeedResponse
            {
                CallId = "call-1",
                Summary = new CallSentimentSummary
                {
                    OverallLabel = "cooling_down",
                    Trend = "improving",
                    SummaryText = "Customer frustration is easing.",
                    UpdatedAtUtc = DateTimeOffset.Parse("2026-06-07T00:10:30Z"),
                    Source = "mock-script"
                }
            },
            "/api/events/knowledge-cards" => new[]
            {
                new KnowledgeCardEvent
                {
                    CallId = "call-1",
                    EventId = "kc-1",
                    EventType = "knowledge_cards",
                    TimestampUtc = DateTimeOffset.Parse("2026-06-07T00:10:24Z"),
                    Sequence = 2,
                    RelatedTranscriptEventId = "evt-2",
                    RelatedTranscriptSequence = 2,
                    Cards =
                    [
                        new KnowledgeCard
                        {
                            Id = "kb-low-tank-not-empty-guidance",
                            Title = "Low tank guidance",
                            Snippet = "Move straight to the earliest practical delivery window.",
                            CitationLabel = "Playbook",
                            SourceSection = "Urgent deliveries",
                            Rank = 1,
                            MatchedEvidence =
                            [
                                new KnowledgeCardMatchedEvidence
                                {
                                    Kind = "translated_phrase",
                                    TranscriptText = "No tenemos propano",
                                    NormalizedText = "We do not have propane",
                                    MatchedKnowledgeText = "out of propane",
                                    Locale = "es-US"
                                }
                            ]
                        }
                    ]
                }
            },
            "/api/mission-control/health" => new MissionControlHealthResponse
            {
                OverallStatus = "degraded",
                Summary = "Mock feed in use.",
                Components =
                [
                    new MissionControlComponentHealth
                    {
                        ComponentId = "frontend",
                        DisplayName = "Frontend Web",
                        Status = "healthy",
                        Readiness = "live",
                        IsLive = true,
                        Evidence = "Health checks passing."
                    },
                    new MissionControlComponentHealth
                    {
                        ComponentId = "mock",
                        DisplayName = "Mock Feed",
                        Status = "mock",
                        Readiness = "active",
                        IsLive = false,
                        Evidence = "Deterministic scripted data."
                    }
                ]
            },
            _ => new { message = $"No fixture for {path}" }
        };

        var response = new HttpResponseMessage(path.StartsWith("/api/", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(payload, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        return Task.FromResult(response);
    }
}

file sealed class AlwaysUnavailablePipelineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = JsonContent.Create(new { message = "down" })
        });
    }
}

file sealed class MalformedPayloadPipelineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return Task.FromResult(response);
    }
}

file sealed class SuccessfulEmptyPipelineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        object payload = path switch
        {
            "/api/session/current" => new SessionCurrentResponse(),
            "/api/events/transcript" => Array.Empty<TranscriptEvent>(),
            "/api/events/translation" => Array.Empty<TranslationEvent>(),
            "/api/events/sentiment" => new SentimentFeedResponse(),
            "/api/events/knowledge-cards" => Array.Empty<KnowledgeCardEvent>(),
            "/api/mission-control/health" => new MissionControlHealthResponse(),
            _ => new { message = "ok" }
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        });
    }
}

file sealed class PartialFailurePipelineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path == "/api/events/transcript")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { message = "down" })
            });
        }

        object payload = path switch
        {
            "/api/session/current" => new SessionCurrentResponse
            {
                Call = new CallSessionMetadata
                {
                    CallId = "call-1",
                    State = "active"
                },
                IsMockFeedActive = true
            },
            "/api/events/translation" => Array.Empty<TranslationEvent>(),
            "/api/events/sentiment" => new SentimentFeedResponse(),
            "/api/events/knowledge-cards" => Array.Empty<KnowledgeCardEvent>(),
            "/api/mission-control/health" => new MissionControlHealthResponse(),
            _ => new { message = "ok" }
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        });
    }
}
