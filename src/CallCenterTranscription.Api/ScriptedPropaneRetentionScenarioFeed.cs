using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Api;

public interface IScriptedScenarioFeed
{
    SessionCurrentResponse GetCurrentSession();
    PipelineCurrentStateResponse GetCurrentState();
    IReadOnlyList<TranscriptEvent> GetTranscriptEvents();
    IReadOnlyList<TranslationEvent> GetTranslationEvents();
    SentimentFeedResponse GetSentimentFeed();
    IReadOnlyList<ChurnRiskEvent> GetChurnRiskEvents();
    IReadOnlyList<KnowledgeCardEvent> GetKnowledgeCardEvents();
    IReadOnlyList<NextBestActionEvent> GetNextBestActionEvents();
    MissionControlHealthResponse GetMissionControlHealth();
}

public sealed class ScriptedPropaneRetentionScenarioFeed : IScriptedScenarioFeed
{
    private const string CallId = "call-propane-retention-0001";
    private const string SessionId = "session-propane-retention-0001";
    private const string ScriptSource = "mock-script:propane-retention-v1";
    private static readonly DateTimeOffset CallStartUtc = DateTimeOffset.Parse("2026-06-07T00:10:18Z");

    private static readonly IReadOnlyList<TranscriptEvent> TranscriptEvents =
    [
        new()
        {
            CallId = CallId,
            EventId = "evt-transcript-0001",
            Sequence = 1,
            UtteranceId = "utt-0001",
            TimestampUtc = CallStartUtc,
            SpeakerId = "agent-sam-holt",
            SpeakerDisplayLabel = "Speaker 1",
            SpeakerRole = "agent",
            SpeakerLabelSource = "scripted",
            Text = "Thank you for calling Valley Fuel support. This is Sam. Am I speaking with Maria Alvarez?",
            Confidence = 0.99,
            DetectedLanguage = "en",
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-transcript-0002",
            Sequence = 2,
            UtteranceId = "utt-0002",
            TimestampUtc = CallStartUtc.AddSeconds(8),
            SpeakerId = "customer-maria-alvarez",
            SpeakerDisplayLabel = "Speaker 2",
            SpeakerRole = "customer",
            SpeakerLabelSource = "scripted",
            Text = "Yes, this is Maria. My delivery was missed and my bill jumped almost forty dollars.",
            Confidence = 0.98,
            DetectedLanguage = "en",
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-transcript-0003",
            Sequence = 3,
            UtteranceId = "utt-0003",
            TimestampUtc = CallStartUtc.AddSeconds(15),
            SpeakerId = "customer-maria-alvarez",
            SpeakerDisplayLabel = "Speaker 2",
            SpeakerRole = "customer",
            SpeakerLabelSource = "scripted",
            Text = "Además, me llegó un volante de NorthStar Propane con un precio mucho más bajo.",
            Confidence = 0.96,
            DetectedLanguage = "es",
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-transcript-0004",
            Sequence = 4,
            UtteranceId = "utt-0004",
            TimestampUtc = CallStartUtc.AddSeconds(24),
            SpeakerId = "agent-sam-holt",
            SpeakerDisplayLabel = "Speaker 1",
            SpeakerRole = "agent",
            SpeakerLabelSource = "scripted",
            Text = "I understand why you're frustrated. I can apply a service credit today and move you to budget billing to stabilize the monthly cost.",
            Confidence = 0.99,
            DetectedLanguage = "en",
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-transcript-0005",
            Sequence = 5,
            UtteranceId = "utt-0005",
            TimestampUtc = CallStartUtc.AddSeconds(32),
            SpeakerId = "customer-maria-alvarez",
            SpeakerDisplayLabel = "Speaker 2",
            SpeakerRole = "customer",
            SpeakerLabelSource = "scripted",
            Text = "If you can do the credit and budget billing now, I'll stay with Valley Fuel.",
            Confidence = 0.97,
            DetectedLanguage = "en",
            Source = ScriptSource
        }
    ];

    private static readonly IReadOnlyList<TranslationEvent> TranslationEvents =
    [
        new()
        {
            CallId = CallId,
            EventId = "evt-translation-0001",
            Sequence = 1,
            UtteranceId = "utt-0003",
            RelatedTranscriptEventId = "evt-transcript-0003",
            RelatedTranscriptSequence = 3,
            TimestampUtc = CallStartUtc.AddSeconds(16),
            SourceLanguage = "es",
            TargetLanguage = "en",
            OriginalText = "Además, me llegó un volante de NorthStar Propane con un precio mucho más bajo.",
            TranslatedText = "Also, I got a flyer from NorthStar Propane with a much lower price.",
            Source = ScriptSource
        }
    ];

    private static readonly CallSentimentSummary SentimentSummary = new()
    {
        CallId = CallId,
        OverallLabel = "cooling_down",
        Trend = "improving",
        SummaryText = "Customer started upset due to missed delivery and bill jump, then accepted service credit and budget billing.",
        UpdatedAtUtc = CallStartUtc.AddSeconds(33),
        Source = ScriptSource
    };

    private static readonly IReadOnlyList<SentimentEvent> SentimentEvents =
    [
        new()
        {
            CallId = CallId,
            EventId = "evt-sentiment-0001",
            RelatedTranscriptEventId = "evt-transcript-0002",
            RelatedTranscriptSequence = 2,
            UtteranceId = "utt-0002",
            TimestampUtc = CallStartUtc.AddSeconds(9),
            Label = "negative",
            Trend = "worsening",
            Score = -0.74,
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-sentiment-0002",
            RelatedTranscriptEventId = "evt-transcript-0003",
            RelatedTranscriptSequence = 3,
            UtteranceId = "utt-0003",
            TimestampUtc = CallStartUtc.AddSeconds(16),
            Label = "negative",
            Trend = "steady",
            Score = -0.78,
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-sentiment-0003",
            RelatedTranscriptEventId = "evt-transcript-0005",
            RelatedTranscriptSequence = 5,
            UtteranceId = "utt-0005",
            TimestampUtc = CallStartUtc.AddSeconds(33),
            Label = "mixed",
            Trend = "improving",
            Score = 0.28,
            Source = ScriptSource
        }
    ];

    private static readonly IReadOnlyList<ChurnRiskEvent> ChurnRiskEvents =
    [
        new()
        {
            CallId = CallId,
            EventId = "evt-churn-risk-0001",
            Sequence = 1,
            UtteranceId = "utt-0003",
            RelatedTranscriptEventId = "evt-transcript-0003",
            RelatedTranscriptSequence = 3,
            TimestampUtc = CallStartUtc.AddSeconds(17),
            RiskLevel = "high",
            RiskScore = 0.86,
            Rationale = "Customer references a lower competitor offer after delivery and billing issues.",
            Source = ScriptSource
        },
        new()
        {
            CallId = CallId,
            EventId = "evt-churn-risk-0002",
            Sequence = 2,
            UtteranceId = "utt-0005",
            RelatedTranscriptEventId = "evt-transcript-0005",
            RelatedTranscriptSequence = 5,
            TimestampUtc = CallStartUtc.AddSeconds(33),
            RiskLevel = "moderate",
            RiskScore = 0.42,
            Rationale = "Customer agrees to remain after credit and budget billing are offered.",
            Source = ScriptSource
        }
    ];

    private static readonly IReadOnlyList<KnowledgeCardEvent> KnowledgeCardEvents =
    [
        new()
        {
            CallId = CallId,
            EventId = "evt-knowledge-card-0001",
            Sequence = 1,
            UtteranceId = "utt-0003",
            RelatedTranscriptEventId = "evt-transcript-0003",
            RelatedTranscriptSequence = 3,
            TimestampUtc = CallStartUtc.AddSeconds(18),
            Source = ScriptSource,
            Cards =
            [
                new()
                {
                    Id = "card-retention-price-match",
                    Title = "Retention policy: competitor price concerns",
                    Snippet = "Offer a service credit and quote budget billing before discussing cancellation.",
                    SourceUrl = "https://contoso.example/policies/retention/price-match"
                }
            ]
        }
    ];

    private static readonly IReadOnlyList<NextBestActionEvent> NextBestActionEvents =
    [
        new()
        {
            CallId = CallId,
            EventId = "evt-nba-0001",
            Sequence = 1,
            UtteranceId = "utt-0003",
            RelatedTranscriptEventId = "evt-transcript-0003",
            RelatedTranscriptSequence = 3,
            TimestampUtc = CallStartUtc.AddSeconds(19),
            Action = "Offer immediate service credit and enroll customer in budget billing.",
            Confidence = 0.91,
            Reasoning = "Customer cites competitor pricing and a missed delivery; proactive retention offer can de-escalate.",
            Source = ScriptSource
        }
    ];

    private static readonly SessionCurrentResponse SessionCurrent = new()
    {
        Call = new CallSessionMetadata
        {
            CallId = CallId,
            SessionId = SessionId,
            CustomerName = "Maria Alvarez",
            CustomerAccountId = "acct-009842",
            AgentName = "Sam Holt",
            QueueName = "Retention Queue",
            State = "active",
            StartedAtUtc = CallStartUtc,
            ScenarioName = "Propane retention - missed delivery save",
            Source = ScriptSource
        },
        SentimentSummary = SentimentSummary,
        IsMockFeedActive = true,
        Notes = "Deterministic scripted feed: missed delivery, bill jump, competitor flyer (Spanish), and save via service credit + budget billing."
    };

    private static readonly MissionControlHealthResponse MissionControlHealth = new()
    {
        OverallStatus = "degraded",
        GeneratedAtUtc = CallStartUtc.AddSeconds(35),
        IsMockFeedActive = true,
        AcsMediaRoutesLiveReady = false,
        Summary = "Mock feed is active for demo reliability. ACS callback/media routes are deferred and not live-ready.",
        Components =
        [
            new()
            {
                ComponentId = "frontend-web",
                DisplayName = "Frontend Web",
                Status = "healthy",
                Readiness = "live",
                IsLive = true,
                Evidence = "App Service deployment health checks are passing.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            },
            new()
            {
                ComponentId = "backend-api",
                DisplayName = "Backend API",
                Status = "healthy",
                Readiness = "live",
                IsLive = true,
                Evidence = "Container App /healthz route is available.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            },
            new()
            {
                ComponentId = "signalr-stream",
                DisplayName = "SignalR Pipeline Hub",
                Status = "healthy",
                Readiness = "live",
                IsLive = true,
                Evidence = "Hub route registered for client negotiation.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            },
            new()
            {
                ComponentId = "mock-feed",
                DisplayName = "Scripted Mock Feed",
                Status = "mock",
                Readiness = "active",
                IsLive = false,
                Evidence = "Propane retention script v1 is serving transcript/translation/sentiment events.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            },
            new()
            {
                ComponentId = "azure-ai-speech",
                DisplayName = "Azure AI Speech",
                Status = "mock",
                Readiness = "deferred-live-validation",
                IsLive = false,
                Evidence = "Diarization labels currently come from deterministic scripted events.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            },
            new()
            {
                ComponentId = "azure-ai-translator",
                DisplayName = "Azure AI Translator",
                Status = "mock",
                Readiness = "deferred-live-validation",
                IsLive = false,
                Evidence = "Spanish translation output is pre-seeded for deterministic demo playback.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            },
            new()
            {
                ComponentId = "acs-media-routes",
                DisplayName = "ACS Callback/Media Routes",
                Status = "deferred",
                Readiness = "not-live-ready",
                IsLive = false,
                Evidence = "Incoming-call callback and media routes are deferred; no live-ready claim.",
                LastCheckedUtc = CallStartUtc.AddSeconds(35)
            }
        ]
    };

    public SessionCurrentResponse GetCurrentSession() => SessionCurrent;

    public PipelineCurrentStateResponse GetCurrentState() => new()
    {
        Call = SessionCurrent.Call,
        SentimentSummary = SentimentSummary,
        IsMockFeedActive = SessionCurrent.IsMockFeedActive,
        GeneratedAtUtc = CallStartUtc.AddSeconds(35),
        StreamReplayPolicy = "full_history_for_active_call",
        TranscriptEvents = TranscriptEvents,
        TranslationEvents = TranslationEvents,
        SentimentEvents = SentimentEvents,
        ChurnRiskEvents = ChurnRiskEvents,
        KnowledgeCardEvents = KnowledgeCardEvents,
        NextBestActionEvents = NextBestActionEvents
    };

    public IReadOnlyList<TranscriptEvent> GetTranscriptEvents() => TranscriptEvents;

    public IReadOnlyList<TranslationEvent> GetTranslationEvents() => TranslationEvents;

    public SentimentFeedResponse GetSentimentFeed() => new()
    {
        CallId = CallId,
        Summary = SentimentSummary,
        Events = SentimentEvents
    };

    public IReadOnlyList<ChurnRiskEvent> GetChurnRiskEvents() => ChurnRiskEvents;

    public IReadOnlyList<KnowledgeCardEvent> GetKnowledgeCardEvents() => KnowledgeCardEvents;

    public IReadOnlyList<NextBestActionEvent> GetNextBestActionEvents() => NextBestActionEvents;

    public MissionControlHealthResponse GetMissionControlHealth() => MissionControlHealth;
}
