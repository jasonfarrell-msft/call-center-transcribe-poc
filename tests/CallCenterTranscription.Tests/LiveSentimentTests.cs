using CallCenterTranscription.Api.Services;
using Xunit;

namespace CallCenterTranscription.Tests;

public sealed class LiveSentimentTests
{
    [Fact]
    public void Lexicon_ScoresPositiveHigherThanNegative()
    {
        var positive = SentimentLexicon.Score("Thank you, that is perfect and very helpful.");
        var negative = SentimentLexicon.Score("This is terrible, I am furious and want to cancel.");

        Assert.True(positive > 0d, $"expected positive score, got {positive}");
        Assert.True(negative < 0d, $"expected negative score, got {negative}");
        Assert.True(positive > negative);
    }

    [Fact]
    public void Lexicon_HonorsNegation()
    {
        var plain = SentimentLexicon.Score("I am happy");
        var negated = SentimentLexicon.Score("I am not happy");

        Assert.True(plain > 0d);
        Assert.True(negated < 0d, $"expected negated score below zero, got {negated}");
    }

    [Fact]
    public void Lexicon_ReturnsZeroForNeutralOrEmpty()
    {
        Assert.Equal(0d, SentimentLexicon.Score(""));
        Assert.Equal(0d, SentimentLexicon.Score("the delivery is scheduled for tuesday"));
    }

    [Fact]
    public void Store_StartsEmptyUntilScoredUtterance()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-1");

        var waiting = store.GetFeed();
        Assert.Empty(waiting.Events);
        Assert.Equal(string.Empty, waiting.CallId);

        // A neutral utterance with no sentiment words does not start the meter.
        store.Append("call-1", "the technician will arrive on tuesday");
        Assert.Empty(store.GetFeed().Events);
    }

    [Fact]
    public void Store_TracksConversationAndOmitsSummary()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-1");

        store.Append("call-1", "I am extremely angry, this is unacceptable.");
        var afterNegative = store.GetFeed();
        Assert.NotEmpty(afterNegative.Events);
        Assert.True(afterNegative.Events[^1].Score < 0d);
        Assert.Equal("negative", afterNegative.Summary.OverallLabel);

        // Conversation recovers — rolling score should rise.
        store.Append("call-1", "Okay, thank you, that works great, I really appreciate it.");
        store.Append("call-1", "Yes that is perfect, wonderful, thanks so much.");
        var afterRecovery = store.GetFeed();

        Assert.True(afterRecovery.Events[^1].Score > afterNegative.Events[^1].Score);
        Assert.Equal("improving", afterRecovery.Summary.Trend);

        // Summary text is intentionally blank in live mode.
        Assert.Equal(string.Empty, afterRecovery.Summary.SummaryText);
    }

    [Fact]
    public void Store_ClearReturnsToWaiting()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-1");
        store.Append("call-1", "this is awful and frustrating");
        Assert.NotEmpty(store.GetFeed().Events);

        store.Clear();
        Assert.Empty(store.GetFeed().Events);
    }

    [Fact]
    public void Store_DropsLateUtteranceAfterCall()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-1");
        store.Append("call-1", "this is awful");
        store.Clear();

        // A late utterance delivered by the Speech SDK after the call ended must not
        // resurrect sentiment between calls.
        store.Append("call-1", "still talking about how terrible it was");
        Assert.Empty(store.GetFeed().Events);
        Assert.Equal(string.Empty, store.GetFeed().CallId);
    }

    [Fact]
    public void Store_RejectsUtteranceFromDifferentCall()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-2");
        store.Append("call-1", "leftover from the previous call, terrible");
        Assert.Empty(store.GetFeed().Events);

        store.Append("call-2", "thank you that is great");
        Assert.NotEmpty(store.GetFeed().Events);
        Assert.Equal("call-2", store.GetFeed().CallId);
    }

    [Fact]
    public void Store_RoutingTreatsNewDiarizationIdAsAmbiguous_DoesNotMoveCustomerSentiment()
    {
        var store = new LiveSentimentStore();
        var attribution = new SpeakerAttributionState();
        const string callId = "call-1";
        store.Reset(callId);

        // First observed speaker is customer.
        attribution.Observe("Guest-1", repAccepted: true);
        if (attribution.IsCustomer("Guest-1"))
        {
            store.Append(callId, "I am frustrated and this is terrible.");
        }

        var afterNegative = store.GetFeed();
        Assert.NotEmpty(afterNegative.Events);
        Assert.True(afterNegative.Events[^1].Score < 0d);
        var eventCountAfterNegative = afterNegative.Events.Count;
        var scoreAfterNegative = afterNegative.Events[^1].Score;

        // Rep speaks.
        attribution.Observe("Guest-2", repAccepted: true);
        Assert.False(attribution.IsCustomer("Guest-2"));

        // Diarization emits a new speaker ID near the end. It must remain ambiguous rather than
        // being guessed as customer via turn-taking, because it could be rep speech.
        attribution.Observe("Guest-2", repAccepted: true); // rep most recent
        attribution.Observe("Guest-3", repAccepted: true); // ambiguous new cluster
        Assert.False(attribution.IsCustomer("Guest-3"));

        if (attribution.IsCustomer("Guest-3"))
        {
            store.Append(callId, "Thank you, this sounds perfect and I appreciate your help.");
        }

        var afterAmbiguous = store.GetFeed();
        Assert.Equal(eventCountAfterNegative, afterAmbiguous.Events.Count);
        Assert.Equal(scoreAfterNegative, afterAmbiguous.Events[^1].Score);
    }
}
