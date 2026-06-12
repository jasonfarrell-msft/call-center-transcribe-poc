using CallCenterTranscription.Ai;
using CallCenterTranscription.Ai.Knowledge;
using CallCenterTranscription.Shared.Events;
using Microsoft.Extensions.Configuration;

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
    private const string DefaultCallId = "call-propane-retention-0001";
    private const string DefaultSessionId = "session-propane-retention-0001";
    private static readonly DateTimeOffset DefaultStartUtc = DateTimeOffset.Parse("2026-06-07T00:10:18Z");

    private static readonly IReadOnlyDictionary<string, ScenarioMetadata> ScenarioMetadataByScriptId =
        new Dictionary<string, ScenarioMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["demo-missed-delivery-bilingual-save"] = new("Elena Morales", "Megan", "Retention Queue"),
            ["demo-low-tank-auto-delivery-conversion"] = new("Jordan Pike", "Darren", "Customer Care Queue"),
            ["demo-renewal-rate-hardship-save"] = new("Harper Sloan", "Ashley", "Renewals Queue")
        };

    private static readonly IReadOnlyDictionary<string, string> TranslationOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["demo-missed-delivery-bilingual-save:3"] = "Also, I got a flyer from NorthStar Propane with a much lower price, so I am thinking about switching."
        };

    private readonly BuiltScenario _scenario;

    public ScriptedPropaneRetentionScenarioFeed()
        : this(new DemoAssistMatcher(), scriptId: null)
    {
    }

    public ScriptedPropaneRetentionScenarioFeed(DemoAssistMatcher matcher, IConfiguration configuration)
        : this(matcher, configuration["DemoScript:ScriptId"])
    {
    }

    public ScriptedPropaneRetentionScenarioFeed(DemoAssistMatcher matcher, string? scriptId)
    {
        _scenario = BuildScenario(matcher, scriptId);
    }

    public SessionCurrentResponse GetCurrentSession() => _scenario.SessionCurrent;
    public PipelineCurrentStateResponse GetCurrentState() => _scenario.CurrentState;
    public IReadOnlyList<TranscriptEvent> GetTranscriptEvents() => _scenario.TranscriptEvents;
    public IReadOnlyList<TranslationEvent> GetTranslationEvents() => _scenario.TranslationEvents;
    public SentimentFeedResponse GetSentimentFeed() => _scenario.SentimentFeed;
    public IReadOnlyList<ChurnRiskEvent> GetChurnRiskEvents() => _scenario.ChurnRiskEvents;
    public IReadOnlyList<KnowledgeCardEvent> GetKnowledgeCardEvents() => _scenario.KnowledgeCardEvents;
    public IReadOnlyList<NextBestActionEvent> GetNextBestActionEvents() => _scenario.NextBestActionEvents;
    public MissionControlHealthResponse GetMissionControlHealth() => _scenario.MissionControlHealth;

    private static BuiltScenario BuildScenario(DemoAssistMatcher matcher, string? scriptId)
    {
        var script = AgentAssistDataLoader.GetScript(scriptId);
        var scopedMatcher = matcher.ForScript(script.ScriptId);
        var metadata = ScenarioMetadataByScriptId.TryGetValue(script.ScriptId, out var value)
            ? value
            : new ScenarioMetadata("Synthetic Customer", "Synthetic Rep", "Support Queue");

        var transcriptEvents = new List<TranscriptEvent>();
        var translationEvents = new List<TranslationEvent>();
        var sentimentEvents = new List<SentimentEvent>();
        var churnRiskEvents = new List<ChurnRiskEvent>();
        var knowledgeCardEvents = new List<KnowledgeCardEvent>();
        var nextBestActionEvents = new List<NextBestActionEvent>();

        var positiveCustomerTurnSeen = false;

        foreach (var turn in script.Turns.OrderBy(static turn => turn.TurnNumber))
        {
            var timestamp = DefaultStartUtc.AddSeconds((turn.TurnNumber - 1) * 8);
            var transcriptEvent = new TranscriptEvent
            {
                CallId = DefaultCallId,
                EventId = $"evt-transcript-{turn.TurnNumber:0000}",
                Sequence = turn.TurnNumber,
                UtteranceId = $"utt-{turn.TurnNumber:0000}",
                TimestampUtc = timestamp,
                SpeakerId = $"{turn.Speaker}-{turn.TurnNumber:0000}",
                SpeakerDisplayLabel = turn.SpeakerLabel,
                SpeakerRole = turn.Speaker,
                SpeakerLabelSource = "scripted-demo",
                Text = turn.Utterance,
                Confidence = 0.99,
                DetectedLanguage = turn.Language,
                Source = $"mock-script:{script.ScriptId}"
            };
            transcriptEvents.Add(transcriptEvent);

            if (!turn.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
                TranslationOverrides.TryGetValue($"{script.ScriptId}:{turn.TurnNumber}", out var translatedText))
            {
                translationEvents.Add(new TranslationEvent
                {
                    CallId = DefaultCallId,
                    EventId = $"evt-translation-{turn.TurnNumber:0000}",
                    Sequence = turn.TurnNumber,
                    UtteranceId = transcriptEvent.UtteranceId,
                    RelatedTranscriptEventId = transcriptEvent.EventId,
                    RelatedTranscriptSequence = transcriptEvent.Sequence,
                    TimestampUtc = timestamp.AddMilliseconds(350),
                    SourceLanguage = turn.Language,
                    TargetLanguage = "en-US",
                    OriginalText = turn.Utterance,
                    TranslatedText = translatedText,
                    Source = $"mock-script:{script.ScriptId}"
                });
            }

            if (!string.Equals(turn.Speaker, "customer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = turn.ExpectedKnowledgeItemIds.Count > 0
                ? scopedMatcher.Match(transcriptEvent)
                : DemoAssistMatchResult.Empty(transcriptEvent);
            if (match.HasMatches)
            {
                churnRiskEvents.Add(match.CreateChurnRiskEvent("demo_trigger_matcher", timestamp.AddMilliseconds(600)));
                knowledgeCardEvents.Add(match.CreateKnowledgeCardEvent("demo_trigger_matcher", timestamp.AddMilliseconds(700)));
                nextBestActionEvents.Add(match.CreateNextBestActionEvent("demo_trigger_matcher", timestamp.AddMilliseconds(800)));
            }

            var sentimentScore = ComputeSentimentScore(turn.Utterance, match.HasMatches);
            var sentimentLabel = sentimentScore switch
            {
                <= -0.25 => "negative",
                >= 0.2 => "positive",
                _ => "mixed"
            };
            if (sentimentScore > 0)
            {
                positiveCustomerTurnSeen = true;
            }

            sentimentEvents.Add(new SentimentEvent
            {
                CallId = DefaultCallId,
                EventId = $"evt-sentiment-{turn.TurnNumber:0000}",
                RelatedTranscriptEventId = transcriptEvent.EventId,
                RelatedTranscriptSequence = transcriptEvent.Sequence,
                UtteranceId = transcriptEvent.UtteranceId,
                TimestampUtc = timestamp.AddMilliseconds(500),
                Label = sentimentLabel,
                Trend = positiveCustomerTurnSeen ? "improving" : "steady",
                Score = sentimentScore,
                Source = $"mock-script:{script.ScriptId}"
            });
        }

        var lastSentiment = sentimentEvents.LastOrDefault();
        var summary = new CallSentimentSummary
        {
            CallId = DefaultCallId,
            OverallLabel = positiveCustomerTurnSeen ? "cooling_down" : "needs_attention",
            Trend = positiveCustomerTurnSeen ? "improving" : "steady",
            SummaryText = positiveCustomerTurnSeen
                ? "Customer concerns were addressed and the call moved toward a save outcome."
                : "Customer concerns remain active and require rep follow-through.",
            UpdatedAtUtc = (lastSentiment?.TimestampUtc ?? DefaultStartUtc).AddMilliseconds(250),
            Source = $"mock-script:{script.ScriptId}"
        };

        var sessionCurrent = new SessionCurrentResponse
        {
            Call = new CallSessionMetadata
            {
                CallId = DefaultCallId,
                SessionId = DefaultSessionId,
                CustomerName = metadata.CustomerName,
                CustomerAccountId = $"acct-{Math.Abs(script.ScriptId.GetHashCode(StringComparison.Ordinal)) % 1000000:000000}",
                AgentName = metadata.AgentName,
                QueueName = metadata.QueueName,
                State = "active",
                StartedAtUtc = DefaultStartUtc,
                ScenarioName = script.ScenarioName,
                Source = $"mock-script:{script.ScriptId}"
            },
            SentimentSummary = summary,
            IsMockFeedActive = true,
            Notes = $"Deterministic scripted feed using {script.ScriptId}. Set DemoScript__ScriptId to switch between the three demo conversations."
        };

        var currentState = new PipelineCurrentStateResponse
        {
            Call = sessionCurrent.Call,
            SentimentSummary = summary,
            IsMockFeedActive = true,
            GeneratedAtUtc = (lastSentiment?.TimestampUtc ?? DefaultStartUtc).AddSeconds(1),
            StreamReplayPolicy = "full_history_for_active_call",
            TranscriptEvents = transcriptEvents,
            TranslationEvents = translationEvents,
            SentimentEvents = sentimentEvents,
            ChurnRiskEvents = churnRiskEvents,
            KnowledgeCardEvents = knowledgeCardEvents,
            NextBestActionEvents = nextBestActionEvents
        };

        var missionControl = BuildMissionControl(script, transcriptEvents.Count, translationEvents.Count, knowledgeCardEvents.Count);
        var sentimentFeed = new SentimentFeedResponse
        {
            CallId = DefaultCallId,
            Summary = summary,
            Events = sentimentEvents
        };

        return new BuiltScenario(
            sessionCurrent,
            currentState,
            transcriptEvents,
            translationEvents,
            sentimentFeed,
            churnRiskEvents,
            knowledgeCardEvents,
            nextBestActionEvents,
            missionControl);
    }

    private static MissionControlHealthResponse BuildMissionControl(
        DemoScriptDefinition script,
        int transcriptCount,
        int translationCount,
        int knowledgeCardCount) => new()
    {
        OverallStatus = "degraded",
        GeneratedAtUtc = DefaultStartUtc.AddMinutes(1),
        IsMockFeedActive = true,
        AcsMediaRoutesLiveReady = false,
        Summary = $"Mock feed is active for demo reliability. {script.ScriptId} is loaded with {knowledgeCardCount} deterministic assist emissions, and ACS callback/media routes are not live-ready.",
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
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            },
            new()
            {
                ComponentId = "backend-api",
                DisplayName = "Backend API",
                Status = "healthy",
                Readiness = "live",
                IsLive = true,
                Evidence = "Container App /healthz route is available.",
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            },
            new()
            {
                ComponentId = "signalr-stream",
                DisplayName = "SignalR Pipeline Hub",
                Status = "healthy",
                Readiness = "live",
                IsLive = true,
                Evidence = "Hub route registered for client negotiation.",
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            },
            new()
            {
                ComponentId = "mock-feed",
                DisplayName = "Scripted Mock Feed",
                Status = "mock",
                Readiness = "active",
                IsLive = false,
                Evidence = $"{script.ScriptId} emits {transcriptCount} transcript turns, {translationCount} translations, and deterministic assist updates.",
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            },
            new()
            {
                ComponentId = "azure-ai-speech",
                DisplayName = "Azure AI Speech",
                Status = "mock",
                Readiness = "deferred-live-validation",
                IsLive = false,
                Evidence = "Diarization labels currently come from deterministic scripted events.",
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            },
            new()
            {
                ComponentId = "azure-ai-translator",
                DisplayName = "Azure AI Translator",
                Status = translationCount > 0 ? "mock" : "deferred",
                Readiness = translationCount > 0 ? "mock-bilingual-support" : "deferred-live-validation",
                IsLive = false,
                Evidence = translationCount > 0
                    ? "Bilingual trigger evidence is pre-seeded for deterministic demo playback."
                    : "Current script is English-only; translator handoff stays dormant.",
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            },
            new()
            {
                ComponentId = "acs-media-routes",
                DisplayName = "ACS Callback/Media Routes",
                Status = "deferred",
                Readiness = "not-live-ready",
                IsLive = false,
                Evidence = "Incoming-call callback and media routes are deferred; no live-ready claim.",
                LastCheckedUtc = DefaultStartUtc.AddMinutes(1)
            }
        ]
    };

    private static double ComputeSentimentScore(string utterance, bool hasAssistMatch)
    {
        var normalized = utterance.ToLowerInvariant();
        if (normalized.Contains("i'll stay", StringComparison.Ordinal) ||
            normalized.Contains("ill stay", StringComparison.Ordinal) ||
            normalized.Contains("go ahead", StringComparison.Ordinal) ||
            normalized.Contains("that works", StringComparison.Ordinal) ||
            normalized.Contains("renew it", StringComparison.Ordinal))
        {
            return 0.42;
        }

        if (normalized.Contains("thinking about switching", StringComparison.Ordinal) ||
            normalized.Contains("may need to leave", StringComparison.Ordinal) ||
            normalized.Contains("cannot pay", StringComparison.Ordinal) ||
            normalized.Contains("missed", StringComparison.Ordinal) ||
            normalized.Contains("run out", StringComparison.Ordinal))
        {
            return -0.62;
        }

        return hasAssistMatch ? -0.28 : 0.05;
    }

    private sealed record ScenarioMetadata(string CustomerName, string AgentName, string QueueName);

    private sealed record BuiltScenario(
        SessionCurrentResponse SessionCurrent,
        PipelineCurrentStateResponse CurrentState,
        IReadOnlyList<TranscriptEvent> TranscriptEvents,
        IReadOnlyList<TranslationEvent> TranslationEvents,
        SentimentFeedResponse SentimentFeed,
        IReadOnlyList<ChurnRiskEvent> ChurnRiskEvents,
        IReadOnlyList<KnowledgeCardEvent> KnowledgeCardEvents,
        IReadOnlyList<NextBestActionEvent> NextBestActionEvents,
        MissionControlHealthResponse MissionControlHealth);
}
