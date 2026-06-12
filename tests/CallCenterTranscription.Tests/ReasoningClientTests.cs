using CallCenterTranscription.Ai;
using CallCenterTranscription.Ai.Knowledge;
using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Tests;

public sealed class ReasoningClientTests
{
    [Fact]
    public async Task MockReasoningClient_ProducesGroundedAssistEvents_ForCustomerTurn()
    {
        var client = new MockReasoningClient();
        var transcript = new TranscriptEvent
        {
            CallId = "call-123",
            EventId = "evt-transcript-1",
            Sequence = 1,
            UtteranceId = "utt-1",
            SpeakerRole = "customer",
            Text = "You missed my delivery yesterday, and I took off work for that window."
        };

        var events = new List<IRealtimeEvent>();
        await foreach (var evt in client.ProcessTranscriptAsync(transcript))
        {
            events.Add(evt);
        }

        var churn = Assert.IsType<ChurnRiskEvent>(events.Single(evt => evt is ChurnRiskEvent));
        var cards = Assert.IsType<KnowledgeCardEvent>(events.Single(evt => evt is KnowledgeCardEvent));
        var action = Assert.IsType<NextBestActionEvent>(events.Single(evt => evt is NextBestActionEvent));

        Assert.Equal("call-123", churn.CallId);
        Assert.Equal("kb-delivery-missed-window-save", cards.Cards.Single().Id);
        Assert.Equal("Synthetic Mock Propane Agent Assist Playbook > service-recovery > Missed delivery window with save offer", cards.Cards.Single().CitationLabel);
        Assert.Equal("service-recovery/missed-delivery", cards.Cards.Single().SourceSection);
        Assert.NotEmpty(cards.Cards.Single().MatchedEvidence);
        Assert.InRange(action.Confidence, 0.01, 0.99);
        Assert.Contains("Book the earliest available delivery window", action.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MockReasoningClient_DropsAssistForRepTurn()
    {
        var client = new MockReasoningClient();
        var transcript = new TranscriptEvent
        {
            CallId = "call-123",
            EventId = "evt-transcript-1",
            Sequence = 1,
            UtteranceId = "utt-1",
            SpeakerRole = "agent",
            Text = "I can lock in the fastest replacement window for you."
        };

        var events = new List<IRealtimeEvent>();
        await foreach (var evt in client.ProcessTranscriptAsync(transcript))
        {
            events.Add(evt);
        }

        Assert.Empty(events);
    }

    [Fact]
    public void DemoAssistMatcher_RecognizesDefinedScriptTriggers_WithExpectedOrdering()
    {
        var matcher = new DemoAssistMatcher();
        var triggerExpectations = AgentAssistDataLoader.ExpectationsByScriptId;

        foreach (var script in AgentAssistDataLoader.Scripts)
        {
            var turnsByNumber = script.Turns.ToDictionary(static turn => turn.TurnNumber);
            foreach (var expectation in triggerExpectations[script.ScriptId])
            {
                var turn = turnsByNumber[expectation.TurnNumber];
                var transcript = new TranscriptEvent
                {
                    CallId = "call-demo",
                    EventId = $"evt-{script.ScriptId}-{turn.TurnNumber}",
                    Sequence = turn.TurnNumber,
                    UtteranceId = $"utt-{script.ScriptId}-{turn.TurnNumber}",
                    SpeakerRole = turn.Speaker,
                    Text = turn.Utterance,
                    DetectedLanguage = turn.Language
                };

                var match = matcher.Match(transcript);

                Assert.True(match.HasMatches, $"Expected assist match for {script.ScriptId} turn {turn.TurnNumber}.");
                Assert.Equal(
                    expectation.ExpectedCards.Select(static card => card.KnowledgeItemId),
                    match.Matches.Select(static card => card.KnowledgeItem.Id));
                Assert.All(match.Matches, card =>
                {
                    Assert.True(card.Rank >= 1);
                    Assert.NotEmpty(card.Evidence);
                });
            }
        }
    }
}
