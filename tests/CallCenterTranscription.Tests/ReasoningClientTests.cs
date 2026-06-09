using CallCenterTranscription.Ai;
using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Tests;

public sealed class ReasoningClientTests
{
    [Fact]
    public async Task MockReasoningClient_ProducesGroundedAssistEvents()
    {
        var client = new MockReasoningClient();
        var transcript = new TranscriptEvent
        {
            CallId = "call-123",
            EventId = "evt-transcript-1",
            Sequence = 1,
            UtteranceId = "utt-1",
            Text = "My bill jumped and a competitor price is much lower."
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
        Assert.InRange(churn.RiskScore, 0.01, 0.99);
        Assert.Contains("Grounding:", churn.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cards.Cards);
        Assert.InRange(action.Confidence, 0.01, 0.99);
        Assert.Contains("Grounding:", action.Reasoning, StringComparison.OrdinalIgnoreCase);
    }
}
