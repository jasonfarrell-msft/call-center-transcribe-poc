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

        var cards = KiraContentPack.Retrieve(transcriptEvent.Text, maxCards: 2);
        var normalizedUtterance = transcriptEvent.Text?.ToLowerInvariant() ?? string.Empty;
        var mentionsCompetitor = normalizedUtterance.Contains("competitor", StringComparison.Ordinal) ||
                                 normalizedUtterance.Contains("northstar", StringComparison.Ordinal) ||
                                 normalizedUtterance.Contains("lower", StringComparison.Ordinal) ||
                                 normalizedUtterance.Contains("volante", StringComparison.Ordinal);
        var mentionsServiceFailure = normalizedUtterance.Contains("missed", StringComparison.Ordinal) ||
                                     normalizedUtterance.Contains("delivery", StringComparison.Ordinal) ||
                                     normalizedUtterance.Contains("bill", StringComparison.Ordinal) ||
                                     normalizedUtterance.Contains("jump", StringComparison.Ordinal);
        var positiveResolutionSignal = normalizedUtterance.Contains("stay", StringComparison.Ordinal) ||
                                       normalizedUtterance.Contains("credit", StringComparison.Ordinal) ||
                                       normalizedUtterance.Contains("budget billing", StringComparison.Ordinal);

        var riskScore = 0.25;
        if (mentionsCompetitor)
        {
            riskScore += 0.45;
        }

        if (mentionsServiceFailure)
        {
            riskScore += 0.2;
        }

        if (positiveResolutionSignal)
        {
            riskScore -= 0.35;
        }

        riskScore = Math.Clamp(riskScore, 0.05, 0.95);
        var riskLevel = riskScore switch
        {
            >= 0.75 => "high",
            >= 0.45 => "moderate",
            _ => "low"
        };

        var groundingTitles = string.Join("; ", cards.Select(card => card.Title));
        var recommendedAction = cards[0].RecommendedAction;
        var confidence = Math.Clamp(0.55 + (cards.Count * 0.1) + (riskScore >= 0.75 ? 0.1 : 0), 0.35, 0.95);

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
            CallId = transcriptEvent.CallId,
            EventId = $"evt-churn-risk-mock-{transcriptEvent.Sequence}",
            Sequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            RiskLevel = riskLevel,
            RiskScore = riskScore,
            Rationale = $"Signals from customer turn indicate {riskLevel} churn risk. Grounding: {groundingTitles}.",
            Source = "mock-reasoning-client"
        };

        yield return new KnowledgeCardEvent
        {
            CallId = transcriptEvent.CallId,
            EventId = $"evt-knowledge-card-mock-{transcriptEvent.Sequence}",
            Sequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            Cards = KiraContentPack.ToKnowledgeCards(cards),
            Source = "mock-reasoning-client"
        };

        yield return new NextBestActionEvent
        {
            CallId = transcriptEvent.CallId,
            EventId = $"evt-nba-mock-{transcriptEvent.Sequence}",
            Sequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            Action = recommendedAction,
            Confidence = confidence,
            Reasoning = $"Recommended from Kira retention content due to call signals. Grounding: {groundingTitles}.",
            Source = "mock-reasoning-client"
        };
    }
}
