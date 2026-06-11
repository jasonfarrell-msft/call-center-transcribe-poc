using CallCenterTranscription.Ai;
using CallCenterTranscription.Ai.Knowledge;

namespace CallCenterTranscription.Tests;

public sealed class SyntheticCorpusTests
{
    [Fact]
    public void Corpus_Loads_WithoutException()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        Assert.NotNull(corpus);
    }

    [Fact]
    public void Corpus_HasExpectedSchemaVersion()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        Assert.Equal("1.0", corpus.SchemaVersion);
    }

    [Fact]
    public void Corpus_ContainsFiveEntries()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        Assert.Equal(5, corpus.Entries.Count);
    }

    [Fact]
    public void Corpus_AllEntries_HaveRequiredFields()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        foreach (var entry in corpus.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id), $"Entry '{entry.Id}' has empty Id.");
            Assert.True(entry.Id.StartsWith("card-", StringComparison.Ordinal), $"Id '{entry.Id}' does not start with 'card-'.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Title), $"Entry '{entry.Id}' has empty Title.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Content), $"Entry '{entry.Id}' has empty Content.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Snippet), $"Entry '{entry.Id}' has empty Snippet.");
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceUrl), $"Entry '{entry.Id}' has empty SourceUrl.");
            Assert.False(string.IsNullOrWhiteSpace(entry.RecommendedAction), $"Entry '{entry.Id}' has empty RecommendedAction.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Category), $"Entry '{entry.Id}' has empty Category.");
            Assert.False(string.IsNullOrWhiteSpace(entry.RiskLevel), $"Entry '{entry.Id}' has empty RiskLevel.");
            Assert.NotEmpty(entry.Tags.Keywords);
            Assert.NotEmpty(entry.Tags.Intents);
            Assert.NotEmpty(entry.Tags.Entities);
        }
    }

    [Fact]
    public void Corpus_AllEntryIds_AreUnique()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        var ids = corpus.Entries.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Corpus_ContainsExpectedScenarioIds()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        var ids = corpus.Entries.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("card-retention-price-match", ids);
        Assert.Contains("card-emergency-out-of-gas", ids);
        Assert.Contains("card-contract-renewal-rate-hike", ids);
        Assert.Contains("card-safety-leak-odor", ids);
        Assert.Contains("card-billing-hardship-payment-plan", ids);
    }

    [Fact]
    public void Corpus_SafetyCard_RequiresEscalation()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        var safetyCard = corpus.Entries.Single(e => e.Id == "card-safety-leak-odor");
        Assert.True(safetyCard.EscalationRequired);
        Assert.Equal("critical", safetyCard.RiskLevel);
    }

    [Fact]
    public void Corpus_EmergencyOutOfGas_IsCriticalRisk_WithoutEscalation()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        var card = corpus.Entries.Single(e => e.Id == "card-emergency-out-of-gas");
        Assert.Equal("critical", card.RiskLevel);
        Assert.False(card.EscalationRequired);
    }

    [Fact]
    public void Corpus_NonSafetyCards_DoNotRequireEscalation()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        var nonSafetyCards = corpus.Entries.Where(e => e.Id != "card-safety-leak-odor");
        Assert.All(nonSafetyCards, e => Assert.False(e.EscalationRequired, $"Card '{e.Id}' unexpectedly requires escalation."));
    }

    [Fact]
    public void KiraContentPack_RetrievesCompetitorCard_WhenTranscriptMentionsCompetitor()
    {
        var results = KiraContentPack.Retrieve("The customer got a flyer from NorthStar with a lower price.");
        Assert.Contains(results, c => c.Id == "card-retention-price-match");
    }

    [Fact]
    public void KiraContentPack_RetrievesBillingCard_WhenTranscriptMentionsBillJump()
    {
        var results = KiraContentPack.Retrieve("My bill jumped almost forty dollars this month.");
        Assert.Contains(results, c => c.Id == "card-billing-hardship-payment-plan");
    }

    [Fact]
    public void KiraContentPack_RetrievesEmergencyCard_WhenTranscriptMentionsOutOfGas()
    {
        var results = KiraContentPack.Retrieve("We are completely out of propane and have no heat.");
        Assert.Contains(results, c => c.Id == "card-emergency-out-of-gas");
    }

    [Fact]
    public void KiraContentPack_RetrievesSafetyCard_WhenTranscriptMentionsGasSmell()
    {
        var results = KiraContentPack.Retrieve("There is a strong gas smell near the tank outside.");
        Assert.Contains(results, c => c.Id == "card-safety-leak-odor");
    }

    [Fact]
    public void KiraContentPack_RetrievesContractCard_WhenTranscriptMentionsRenewal()
    {
        var results = KiraContentPack.Retrieve("I received my contract renewal and the rate is higher than last year.");
        Assert.Contains(results, c => c.Id == "card-contract-renewal-rate-hike");
    }

    [Fact]
    public void KiraContentPack_ToKnowledgeCards_MapsAllFields()
    {
        var corpus = SyntheticCorpusLoader.Corpus;
        var entries = corpus.Entries.Take(1).Select(e => new KiraContentPackEntry(
            e.Id, e.Title, e.Snippet, e.SourceUrl, e.RecommendedAction, e.Tags.Keywords));

        var cards = KiraContentPack.ToKnowledgeCards(entries);

        Assert.Single(cards);
        var card = cards[0];
        Assert.Equal("card-retention-price-match", card.Id);
        Assert.False(string.IsNullOrWhiteSpace(card.Title));
        Assert.False(string.IsNullOrWhiteSpace(card.Snippet));
        Assert.False(string.IsNullOrWhiteSpace(card.SourceUrl));
    }

    [Fact]
    public void SyntheticCorpusLoader_ReturnsSameInstance_OnMultipleCalls()
    {
        var first = SyntheticCorpusLoader.Corpus;
        var second = SyntheticCorpusLoader.Corpus;
        Assert.Same(first, second);
    }
}
