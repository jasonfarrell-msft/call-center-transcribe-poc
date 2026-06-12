using CallCenterTranscription.Ai;
using CallCenterTranscription.Ai.Knowledge;
using CallCenterTranscription.Api;

namespace CallCenterTranscription.Tests;

public sealed class DemoScriptedScenarioFeedTests
{
    [Fact]
    public void Feed_DefaultScenario_ReplaysEvidenceBackedAssistCards()
    {
        var feed = new ScriptedPropaneRetentionScenarioFeed();
        var state = feed.GetCurrentState();

        Assert.Equal(6, state.TranscriptEvents.Count);
        Assert.Single(state.TranslationEvents);
        Assert.Equal(3, state.KnowledgeCardEvents.Count);
        Assert.Equal(3, state.NextBestActionEvents.Count);
        Assert.All(state.KnowledgeCardEvents.SelectMany(static evt => evt.Cards), card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.CitationLabel));
            Assert.False(string.IsNullOrWhiteSpace(card.SourceSection));
            Assert.NotEmpty(card.MatchedEvidence);
        });
    }

    public static IEnumerable<object[]> ScriptIds() =>
        AgentAssistDataLoader.Scripts.Select(static script => new object[] { script.ScriptId });

    [Theory]
    [MemberData(nameof(ScriptIds))]
    public void Feed_AllDefinedScripts_SurfaceExpectedKnowledgeCardsWithUsableMetadata(string scriptId)
    {
        var feed = new ScriptedPropaneRetentionScenarioFeed(new DemoAssistMatcher(), scriptId);
        var state = feed.GetCurrentState();
        var script = AgentAssistDataLoader.GetScript(scriptId);
        var expectations = Assert.Single(
            AgentAssistDataLoader.ExpectationsByScriptId.Where(static pair => !string.IsNullOrWhiteSpace(pair.Key)),
            pair => string.Equals(pair.Key, scriptId, StringComparison.OrdinalIgnoreCase)).Value;

        Assert.NotEmpty(expectations);
        Assert.Equal(expectations.Count, state.KnowledgeCardEvents.Count);

        var knowledgeEventsBySequence = state.KnowledgeCardEvents.ToDictionary(evt => (int)evt.Sequence);
        var transcriptEventsBySequence = state.TranscriptEvents.ToDictionary(evt => (int)evt.Sequence);

        foreach (var expectedTurn in expectations)
        {
            Assert.True(
                transcriptEventsBySequence.TryGetValue(expectedTurn.TurnNumber, out var transcript),
                $"Missing transcript event for script '{scriptId}' turn {expectedTurn.TurnNumber}.");
            Assert.True(
                knowledgeEventsBySequence.TryGetValue(expectedTurn.TurnNumber, out var knowledgeEvent),
                $"Missing knowledge-card event for script '{scriptId}' turn {expectedTurn.TurnNumber}.");

            Assert.Equal("customer", transcript.SpeakerRole);
            Assert.Equal(transcript.UtteranceId, knowledgeEvent.UtteranceId);
            Assert.Equal(transcript.EventId, knowledgeEvent.RelatedTranscriptEventId);
            Assert.Equal(transcript.Sequence, knowledgeEvent.RelatedTranscriptSequence);

            var expectedCardIds = expectedTurn.ExpectedCards
                .OrderBy(card => card.Rank)
                .Select(card => card.KnowledgeItemId)
                .ToArray();
            var actualCardIds = knowledgeEvent.Cards
                .OrderBy(card => card.Rank)
                .Select(card => card.Id)
                .ToArray();

            Assert.Equal(expectedCardIds, actualCardIds);

            foreach (var expectedCard in expectedTurn.ExpectedCards)
            {
                var actualCard = Assert.Single(knowledgeEvent.Cards, card => string.Equals(card.Id, expectedCard.KnowledgeItemId, StringComparison.OrdinalIgnoreCase));
                var knowledgeItem = AgentAssistDataLoader.GetKnowledgeItem(expectedCard.KnowledgeItemId);

                Assert.Equal(expectedCard.Rank, actualCard.Rank);
                Assert.Equal(
                    string.IsNullOrWhiteSpace(knowledgeItem.RepGuidance) ? knowledgeItem.Answer : knowledgeItem.RepGuidance,
                    actualCard.Snippet);
                Assert.Equal(knowledgeItem.SourceUri, actualCard.SourceUrl);
                Assert.Equal(knowledgeItem.CitationLabel, actualCard.CitationLabel);
                Assert.Equal(knowledgeItem.SourceSection, actualCard.SourceSection);
                Assert.NotEmpty(actualCard.MatchedEvidence);

                var actualEvidence = actualCard.MatchedEvidence
                    .Select(static evidence => (evidence.Kind, evidence.TranscriptText, evidence.NormalizedText, evidence.MatchedKnowledgeText, evidence.Locale))
                    .ToArray();
                var expectedEvidence = expectedCard.MatchedEvidence
                    .Select(static evidence => (evidence.Kind, evidence.TranscriptText, evidence.NormalizedText, evidence.MatchedKnowledgeText, evidence.Locale))
                    .ToArray();

                Assert.Equal(expectedEvidence, actualEvidence);
            }
        }

        Assert.Contains(
            script.Turns,
            turn => string.Equals(turn.Speaker, "customer", StringComparison.OrdinalIgnoreCase) &&
                    turn.ExpectedKnowledgeItemIds.Count > 0 &&
                    knowledgeEventsBySequence.ContainsKey(turn.TurnNumber));

        Assert.DoesNotContain(
            script.Turns,
            turn => string.Equals(turn.Speaker, "customer", StringComparison.OrdinalIgnoreCase) &&
                    turn.ExpectedKnowledgeItemIds.Count == 0 &&
                    knowledgeEventsBySequence.ContainsKey(turn.TurnNumber));
    }

    [Fact]
    public void Feed_DefaultScenario_PreservesTranslationBackedEvidenceForSpanishTrigger()
    {
        var feed = new ScriptedPropaneRetentionScenarioFeed();
        var state = feed.GetCurrentState();

        var translation = Assert.Single(state.TranslationEvents);
        Assert.Equal("es-US", translation.SourceLanguage);

        var knowledgeEvent = Assert.Single(state.KnowledgeCardEvents, static evt => evt.Sequence == 3);
        var competitorCard = Assert.Single(knowledgeEvent.Cards);
        var translatedEvidence = competitorCard.MatchedEvidence.Where(static evidence => evidence.Kind.StartsWith("translated_", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(translatedEvidence);
        Assert.All(translatedEvidence, evidence =>
        {
            Assert.Equal("es-US", evidence.Locale);
            Assert.False(string.IsNullOrWhiteSpace(evidence.TranscriptText));
            Assert.False(string.IsNullOrWhiteSpace(evidence.NormalizedText));
        });
        Assert.Contains(
            translatedEvidence,
            evidence => !string.Equals(evidence.TranscriptText, evidence.NormalizedText, StringComparison.Ordinal));
    }

    [Fact]
    public void Feed_LowTankConversion_UpdatesSentimentBeforeFinalAcceptanceAndEndsResolved()
    {
        var feed = new ScriptedPropaneRetentionScenarioFeed(
            new DemoAssistMatcher(),
            "demo-low-tank-auto-delivery-conversion");
        var sentiment = feed.GetSentimentFeed();

        var earlyRecovery = Assert.Single(
            sentiment.Events,
            evt => evt.RelatedTranscriptSequence == 4);
        Assert.Equal("improving", earlyRecovery.Trend);
        Assert.True(earlyRecovery.Score > -1d, "rep recovery offer should improve the score before the customer agrees to the plan");

        var preClosePositive = Assert.Single(
            sentiment.Events,
            evt => evt.RelatedTranscriptSequence == 6);
        Assert.Equal("positive", preClosePositive.Label);
        Assert.True(preClosePositive.Score > 0d);

        var finalResolution = Assert.Single(
            sentiment.Events,
            evt => evt.RelatedTranscriptSequence == 7);
        Assert.Equal("positive", finalResolution.Label);
        Assert.True(finalResolution.Score > 0d);
        Assert.Equal("resolved", sentiment.Summary.OverallLabel);
        Assert.Contains("converted", sentiment.Summary.SummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.True(sentiment.Events[^1].Score > 0d);
    }
}
