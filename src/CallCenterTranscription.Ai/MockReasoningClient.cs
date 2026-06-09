using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Ai;

public sealed class MockReasoningClient : IReasoningClient
{
    public async IAsyncEnumerable<IRealtimeEvent> ProcessTranscriptAsync(
        TranscriptEvent transcriptEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        yield return new SentimentEvent
        {
            CallId = transcriptEvent.CallId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            Label = "neutral",
            Trend = "steady",
            Score = 0.5,
            Source = "mock-reasoning-client"
        };

        yield return new ChurnRiskEvent
        {
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            RiskLevel = "low",
            RiskScore = 0.2,
            Rationale = "Phase 0 mock reasoning client response."
        };

        yield return new NextBestActionEvent
        {
            Action = "Gather more customer context before offering retention incentives.",
            Confidence = 0.35,
            Reasoning = "Phase 0 placeholder to validate event flow and UI bindings."
        };
    }
}
