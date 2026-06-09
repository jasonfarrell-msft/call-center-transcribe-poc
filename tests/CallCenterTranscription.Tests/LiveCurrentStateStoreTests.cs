using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Shared.Events;
using Microsoft.Extensions.Configuration;

namespace CallCenterTranscription.Tests;

public sealed class LiveCurrentStateStoreTests
{
    [Fact]
    public void ActiveCallStore_CapturesStableStartedAtUntilCleared()
    {
        var store = new ActiveCallStore();
        var startedAt = DateTimeOffset.Parse("2026-06-09T15:30:00Z");

        store.SetCallId("call-live-1", startedAt);

        var snapshot = store.GetSnapshot();
        Assert.Equal("call-live-1", snapshot.CallId);
        Assert.Equal(startedAt, snapshot.StartedAtUtc);

        store.Clear();

        var cleared = store.GetSnapshot();
        Assert.Null(cleared.CallId);
        Assert.Null(cleared.StartedAtUtc);
    }

    [Fact]
    public void PipelineCurrentStateStore_LiveMode_ReplaysAccumulatedLiveHistory()
    {
        var activeCallStore = new ActiveCallStore();
        var liveSentimentStore = new LiveSentimentStore();
        var currentStateStore = CreateStore("Acs", activeCallStore, liveSentimentStore);
        var startedAt = DateTimeOffset.Parse("2026-06-09T16:00:00Z");
        activeCallStore.SetCallId("call-live-1", startedAt);
        currentStateStore.ResetForCall("call-live-1");
        liveSentimentStore.Reset("call-live-1");

        currentStateStore.AppendTranscriptEvent(new TranscriptEvent
        {
            CallId = "call-live-1",
            EventId = "evt-partial-1",
            Sequence = 1,
            UtteranceId = "utt-1",
            Text = "Partial",
            IsFinal = false,
            Source = "test"
        });
        currentStateStore.AppendTranscriptEvent(new TranscriptEvent
        {
            CallId = "call-live-1",
            EventId = "evt-final-1",
            Sequence = 2,
            UtteranceId = "utt-1",
            Text = "I am upset about this missed delivery.",
            IsFinal = true,
            SpeakerDisplayLabel = "Customer",
            SpeakerRole = "customer",
            SpeakerLabelSource = "test",
            DetectedLanguage = "en-US",
            Source = "test"
        });
        currentStateStore.AppendTranslationEvent(new TranslationEvent
        {
            CallId = "call-live-1",
            EventId = "evt-translation-1",
            Sequence = 2,
            UtteranceId = "utt-1",
            RelatedTranscriptEventId = "evt-final-1",
            RelatedTranscriptSequence = 2,
            OriginalText = "I am upset about this missed delivery.",
            TranslatedText = "I am upset about this missed delivery.",
            SourceLanguage = "en",
            TargetLanguage = "en",
            Source = "test"
        });
        currentStateStore.AppendChurnRiskEvent(new ChurnRiskEvent
        {
            CallId = "call-live-1",
            EventId = "evt-churn-1",
            Sequence = 2,
            UtteranceId = "utt-1",
            RelatedTranscriptEventId = "evt-final-1",
            RelatedTranscriptSequence = 2,
            RiskLevel = "high",
            RiskScore = 0.81,
            Rationale = "Customer is upset.",
            Source = "test"
        });
        currentStateStore.AppendKnowledgeCardEvent(new KnowledgeCardEvent
        {
            CallId = "call-live-1",
            EventId = "evt-card-1",
            Sequence = 2,
            UtteranceId = "utt-1",
            RelatedTranscriptEventId = "evt-final-1",
            RelatedTranscriptSequence = 2,
            Cards =
            [
                new KnowledgeCard
                {
                    Id = "card-1",
                    Title = "Retention playbook",
                    Snippet = "Offer recovery credit first."
                }
            ],
            Source = "test"
        });
        currentStateStore.AppendNextBestActionEvent(new NextBestActionEvent
        {
            CallId = "call-live-1",
            EventId = "evt-nba-1",
            Sequence = 2,
            UtteranceId = "utt-1",
            RelatedTranscriptEventId = "evt-final-1",
            RelatedTranscriptSequence = 2,
            Action = "Offer delivery credit",
            Confidence = 0.92,
            Reasoning = "High churn risk after missed delivery.",
            Source = "test"
        });
        liveSentimentStore.Append("call-live-1", "I am upset about this missed delivery.");

        var snapshot = currentStateStore.GetSnapshot();

        Assert.Equal("call-live-1", snapshot.Call.CallId);
        Assert.Equal(startedAt, snapshot.Call.StartedAtUtc);
        Assert.Single(snapshot.TranscriptEvents);
        Assert.Single(snapshot.TranslationEvents);
        Assert.Single(snapshot.ChurnRiskEvents);
        Assert.Single(snapshot.KnowledgeCardEvents);
        Assert.Single(snapshot.NextBestActionEvents);
        Assert.NotEmpty(snapshot.SentimentEvents);
    }

    [Fact]
    public void PipelineCurrentStateStore_MockMode_IgnoresLiveMutations()
    {
        var activeCallStore = new ActiveCallStore();
        var liveSentimentStore = new LiveSentimentStore();
        var currentStateStore = CreateStore("Mock", activeCallStore, liveSentimentStore);

        activeCallStore.SetCallId("call-live-1", DateTimeOffset.Parse("2026-06-09T17:00:00Z"));
        currentStateStore.ResetForCall("call-live-1");
        currentStateStore.AppendTranscriptEvent(new TranscriptEvent
        {
            CallId = "call-live-1",
            EventId = "evt-live-1",
            Sequence = 1,
            UtteranceId = "utt-1",
            Text = "Should not appear",
            IsFinal = true
        });

        var snapshot = currentStateStore.GetSnapshot();

        Assert.True(snapshot.IsMockFeedActive);
        Assert.Equal("call-propane-retention-0001", snapshot.Call.CallId);
        Assert.Equal(5, snapshot.TranscriptEvents.Count);
    }

    private static PipelineCurrentStateStore CreateStore(
        string audioSourceMode,
        ActiveCallStore activeCallStore,
        LiveSentimentStore liveSentimentStore)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AudioSource:Mode"] = audioSourceMode
            })
            .Build();

        return new PipelineCurrentStateStore(
            new ScriptedPropaneRetentionScenarioFeed(),
            activeCallStore,
            liveSentimentStore,
            configuration);
    }
}
