using CallCenterTranscription.Ai;
using CallCenterTranscription.Ai.Knowledge;
using System.Text.Json;

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

    [Fact]
    public void AgentAssistJsonl_AllRecordsExposeIngestionMetadata()
    {
        var path = GetRepoFilePath(
            "src",
            "CallCenterTranscription.Ai",
            "Knowledge",
            "synthetic-agent-assist-knowledge.v1.jsonl");

        var records = new List<(string Id, string DocumentId, int ChunkIndex, int ChunkCount)>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            Assert.False(string.IsNullOrWhiteSpace(line));

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var id = root.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(ids.Add(id!), $"Duplicate JSONL id '{id}'.");

            var documentId = root.GetProperty("document_id").GetString();
            var chunkIndex = root.GetProperty("chunk_index").GetInt32();
            var chunkCount = root.GetProperty("chunk_count").GetInt32();

            Assert.StartsWith("doc-", documentId, StringComparison.Ordinal);
            Assert.InRange(chunkIndex, 0, int.MaxValue);
            Assert.InRange(chunkCount, 1, int.MaxValue);
            Assert.True(chunkIndex < chunkCount, $"Chunk index {chunkIndex} must be less than chunk count {chunkCount} for '{id}'.");
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("retrieval_text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("source_title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("source_section").GetString()));
            Assert.StartsWith("synthetic://", root.GetProperty("source_uri").GetString(), StringComparison.Ordinal);
            Assert.Contains("Synthetic", root.GetProperty("citation_label").GetString(), StringComparison.Ordinal);

            records.Add((id!, documentId!, chunkIndex, chunkCount));
        }

        Assert.NotEmpty(records);
        foreach (var document in records.GroupBy(r => r.DocumentId, StringComparer.OrdinalIgnoreCase))
        {
            var chunkCount = document.First().ChunkCount;
            Assert.All(document, chunk => Assert.Equal(chunkCount, chunk.ChunkCount));

            var orderedIndexes = document.Select(chunk => chunk.ChunkIndex).OrderBy(index => index).ToArray();
            Assert.Equal(chunkCount, orderedIndexes.Length);
            Assert.Equal(Enumerable.Range(0, chunkCount), orderedIndexes);
        }
    }

    [Fact]
    public void AgentAssistSchema_DeclaresPortableIngestionFields()
    {
        var path = GetRepoFilePath(
            "src",
            "CallCenterTranscription.Ai",
            "Knowledge",
            "synthetic-agent-assist-knowledge.schema.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var required = document.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => value is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in new[]
                 {
                     "document_id",
                     "chunk_index",
                     "chunk_count",
                     "retrieval_text",
                     "source_title",
                     "source_section",
                     "source_uri",
                     "citation_label"
                 })
        {
            Assert.Contains(field, required);
        }
    }

    [Fact]
    public void DemoTriggerExpectations_DefineEvidenceBackedKnowledgeCardShape()
    {
        var path = GetRepoFilePath(
            "samples",
            "agent-assist-demo-trigger-expectations.v1.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var eventShape = root.GetProperty("minimal_event_shape");

        Assert.Equal("demo_retrieval_expectations", root.GetProperty("artifact_type").GetString());
        Assert.Equal("stream.knowledgeCards", eventShape.GetProperty("stream").GetString());

        var topLevelFields = eventShape.GetProperty("top_level_fields")
            .EnumerateArray()
            .Select(field => field.GetProperty("field").GetString())
            .Where(field => field is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in new[]
                 {
                     "callId",
                     "eventId",
                     "eventType",
                     "timestampUtc",
                     "sequence",
                     "utteranceId",
                     "relatedTranscriptEventId",
                     "relatedTranscriptSequence",
                     "source",
                     "cards"
                 })
        {
            Assert.Contains(field, topLevelFields);
        }

        var cardFields = eventShape.GetProperty("card_fields")
            .EnumerateArray()
            .Select(field => field.GetProperty("field").GetString())
            .Where(field => field is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in new[]
                 {
                     "id",
                     "title",
                     "snippet",
                     "sourceUrl",
                     "citationLabel",
                     "sourceSection",
                     "rank",
                     "matchedEvidence"
                 })
        {
            Assert.Contains(field, cardFields);
        }

        var evidenceFields = eventShape.GetProperty("matched_evidence_fields")
            .EnumerateArray()
            .Select(field => field.GetProperty("field").GetString())
            .Where(field => field is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in new[]
                 {
                     "kind",
                     "transcriptText",
                     "normalizedText",
                     "matchedKnowledgeText",
                     "locale"
                 })
        {
            Assert.Contains(field, evidenceFields);
        }
    }

    [Fact]
    public void DemoTriggerExpectations_StayAlignedWithScriptsAndKnowledgeItems()
    {
        var expectationPath = GetRepoFilePath(
            "samples",
            "agent-assist-demo-trigger-expectations.v1.json");
        var scriptPath = GetRepoFilePath(
            "samples",
            "agent-assist-demo-scripts.v1.json");
        var knowledgePath = GetRepoFilePath(
            "src",
            "CallCenterTranscription.Ai",
            "Knowledge",
            "synthetic-agent-assist-knowledge.v1.jsonl");

        using var expectationDocument = JsonDocument.Parse(File.ReadAllText(expectationPath));
        using var scriptDocument = JsonDocument.Parse(File.ReadAllText(scriptPath));

        var maxExcerptLength = expectationDocument.RootElement
            .GetProperty("trigger_rules")
            .GetProperty("matched_evidence_excerpt_max_chars")
            .GetInt32();

        var knowledgeIds = File.ReadLines(knowledgePath)
            .Select(line => JsonDocument.Parse(line))
            .Select(document =>
            {
                using (document)
                {
                    return document.RootElement.GetProperty("id").GetString();
                }
            })
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scriptsById = scriptDocument.RootElement.GetProperty("scripts")
            .EnumerateArray()
            .ToDictionary(
                script => script.GetProperty("script_id").GetString()!,
                script => script,
                StringComparer.OrdinalIgnoreCase);

        foreach (var scriptExpectation in expectationDocument.RootElement.GetProperty("scripts").EnumerateArray())
        {
            var scriptId = scriptExpectation.GetProperty("script_id").GetString();
            Assert.True(!string.IsNullOrWhiteSpace(scriptId));
            Assert.True(scriptsById.TryGetValue(scriptId!, out var script), $"Missing demo script '{scriptId}'.");

            var turnsByNumber = script.GetProperty("turns")
                .EnumerateArray()
                .ToDictionary(
                    turn => turn.GetProperty("turn_number").GetInt32(),
                    turn => turn);

            foreach (var triggerExpectation in scriptExpectation.GetProperty("trigger_expectations").EnumerateArray())
            {
                var turnNumber = triggerExpectation.GetProperty("turn_number").GetInt32();
                Assert.True(turnsByNumber.TryGetValue(turnNumber, out var scriptTurn), $"Missing turn {turnNumber} for script '{scriptId}'.");
                Assert.Equal("customer", scriptTurn.GetProperty("speaker").GetString());
                Assert.Equal("customer", triggerExpectation.GetProperty("speaker").GetString());

                var scriptCardIds = scriptTurn.GetProperty("expected_knowledge_item_ids")
                    .EnumerateArray()
                    .Select(id => id.GetString())
                    .Where(id => id is not null)
                    .Cast<string>()
                    .ToArray();

                var expectedCardIds = triggerExpectation.GetProperty("expected_cards")
                    .EnumerateArray()
                    .Select(card => card.GetProperty("knowledge_item_id").GetString())
                    .Where(id => id is not null)
                    .Cast<string>()
                    .ToArray();

                Assert.Equal(scriptCardIds, expectedCardIds);

                foreach (var expectedCard in triggerExpectation.GetProperty("expected_cards").EnumerateArray())
                {
                    var knowledgeItemId = expectedCard.GetProperty("knowledge_item_id").GetString();
                    Assert.False(string.IsNullOrWhiteSpace(knowledgeItemId));
                    Assert.Contains(knowledgeItemId, knowledgeIds);

                    var matchedEvidence = expectedCard.GetProperty("matched_evidence").EnumerateArray().ToArray();
                    Assert.NotEmpty(matchedEvidence);

                    foreach (var evidence in matchedEvidence)
                    {
                        var transcriptText = evidence.GetProperty("transcript_text").GetString();
                        var normalizedText = evidence.GetProperty("normalized_text").GetString();
                        var matchedKnowledgeText = evidence.GetProperty("matched_knowledge_text").GetString();
                        var locale = evidence.GetProperty("locale").GetString();

                        Assert.False(string.IsNullOrWhiteSpace(transcriptText));
                        Assert.False(string.IsNullOrWhiteSpace(normalizedText));
                        Assert.False(string.IsNullOrWhiteSpace(matchedKnowledgeText));
                        Assert.Matches("^[a-z]{2}-[A-Z]{2}$", locale!);
                        Assert.InRange(transcriptText!.Length, 1, maxExcerptLength);
                        Assert.InRange(normalizedText!.Length, 1, maxExcerptLength);
                    }
                }
            }
        }
    }

    private static string GetRepoFilePath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "CallCenterTranscription.sln");
            if (File.Exists(solutionPath))
            {
                return Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
