using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CallCenterTranscription.Ai.Knowledge;

public static class AgentAssistDataLoader
{
    private static readonly Lazy<AgentAssistData> LazyData = new(Load, isThreadSafe: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyDictionary<string, AgentAssistKnowledgeItem> KnowledgeById => LazyData.Value.KnowledgeById;
    public static IReadOnlyList<DemoScriptDefinition> Scripts => LazyData.Value.Scripts;
    public static IReadOnlyDictionary<string, IReadOnlyList<DemoTriggerExpectation>> ExpectationsByScriptId => LazyData.Value.ExpectationsByScriptId;
    public static DemoTriggerRules TriggerRules => LazyData.Value.TriggerRules;

    public static DemoScriptDefinition GetScript(string? scriptId)
    {
        if (!string.IsNullOrWhiteSpace(scriptId))
        {
            var match = Scripts.FirstOrDefault(script => string.Equals(script.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return Scripts[0];
    }

    public static AgentAssistKnowledgeItem GetKnowledgeItem(string knowledgeItemId) =>
        KnowledgeById.TryGetValue(knowledgeItemId, out var item)
            ? item
            : throw new KeyNotFoundException($"Knowledge item '{knowledgeItemId}' was not found in the synthetic agent-assist corpus.");

    private static AgentAssistData Load()
    {
        var assembly = typeof(AgentAssistDataLoader).Assembly;
        var knowledge = LoadKnowledge(assembly);
        var scripts = LoadJsonResource<DemoScriptSet>(assembly, "agent-assist-demo-scripts.v1.json").Scripts;
        var expectationSet = LoadJsonResource<DemoTriggerExpectationSet>(assembly, "agent-assist-demo-trigger-expectations.v1.json");

        var expectationsByScriptId = expectationSet.Scripts
            .ToDictionary(
                static script => script.ScriptId,
                static script => (IReadOnlyList<DemoTriggerExpectation>)script.TriggerExpectations,
                StringComparer.OrdinalIgnoreCase);

        return new AgentAssistData(
            knowledge.ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase),
            scripts,
            expectationsByScriptId,
            expectationSet.TriggerRules);
    }

    private static IReadOnlyList<AgentAssistKnowledgeItem> LoadKnowledge(Assembly assembly)
    {
        var resourceName = FindResourceName(assembly, "synthetic-agent-assist-knowledge.v1.jsonl");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource stream for '{resourceName}'.");
        using var reader = new StreamReader(stream);

        var items = new List<AgentAssistKnowledgeItem>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<AgentAssistKnowledgeItem>(line, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize agent-assist knowledge JSONL record.");
            items.Add(item);
        }

        return items;
    }

    private static T LoadJsonResource<T>(Assembly assembly, string resourceSuffix)
    {
        var resourceName = FindResourceName(assembly, resourceSuffix);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource stream for '{resourceName}'.");

        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize resource '{resourceName}'.");
    }

    private static string FindResourceName(Assembly assembly, string suffix) =>
        assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource ending with '{suffix}' was not found.");

    private sealed record AgentAssistData(
        IReadOnlyDictionary<string, AgentAssistKnowledgeItem> KnowledgeById,
        IReadOnlyList<DemoScriptDefinition> Scripts,
        IReadOnlyDictionary<string, IReadOnlyList<DemoTriggerExpectation>> ExpectationsByScriptId,
        DemoTriggerRules TriggerRules);
}

public sealed class AgentAssistKnowledgeItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("trigger_phrases")]
    public IReadOnlyList<string> TriggerPhrases { get; init; } = [];

    [JsonPropertyName("keywords")]
    public IReadOnlyList<string> Keywords { get; init; } = [];

    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("rep_guidance")]
    public string RepGuidance { get; init; } = string.Empty;

    [JsonPropertyName("next_best_action")]
    public string NextBestAction { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = string.Empty;

    [JsonPropertyName("source_section")]
    public string SourceSection { get; init; } = string.Empty;

    [JsonPropertyName("source_uri")]
    public string SourceUri { get; init; } = string.Empty;

    [JsonPropertyName("citation_label")]
    public string CitationLabel { get; init; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("trigger_locales")]
    public IReadOnlyList<string> TriggerLocales { get; init; } = [];
}

public sealed class DemoScriptSet
{
    [JsonPropertyName("scripts")]
    public IReadOnlyList<DemoScriptDefinition> Scripts { get; init; } = [];
}

public sealed class DemoScriptDefinition
{
    [JsonPropertyName("script_id")]
    public string ScriptId { get; init; } = string.Empty;

    [JsonPropertyName("scenario_name")]
    public string ScenarioName { get; init; } = string.Empty;

    [JsonPropertyName("demo_purpose")]
    public string DemoPurpose { get; init; } = string.Empty;

    [JsonPropertyName("success_criterion")]
    public string SuccessCriterion { get; init; } = string.Empty;

    [JsonPropertyName("turns")]
    public IReadOnlyList<DemoScriptTurn> Turns { get; init; } = [];
}

public sealed class DemoScriptTurn
{
    [JsonPropertyName("turn_number")]
    public int TurnNumber { get; init; }

    [JsonPropertyName("speaker")]
    public string Speaker { get; init; } = string.Empty;

    [JsonPropertyName("speaker_label")]
    public string SpeakerLabel { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [JsonPropertyName("utterance")]
    public string Utterance { get; init; } = string.Empty;

    [JsonPropertyName("expected_knowledge_item_ids")]
    public IReadOnlyList<string> ExpectedKnowledgeItemIds { get; init; } = [];

    [JsonPropertyName("expected_rep_visible_guidance")]
    public IReadOnlyList<string> ExpectedRepVisibleGuidance { get; init; } = [];
}

public sealed class DemoTriggerExpectationSet
{
    [JsonPropertyName("trigger_rules")]
    public DemoTriggerRules TriggerRules { get; init; } = new();

    [JsonPropertyName("scripts")]
    public IReadOnlyList<DemoTriggerExpectationScript> Scripts { get; init; } = [];
}

public sealed class DemoTriggerRules
{
    [JsonPropertyName("customer_turns_only")]
    public bool CustomerTurnsOnly { get; init; } = true;

    [JsonPropertyName("max_cards_per_turn")]
    public int MaxCardsPerTurn { get; init; } = 2;

    [JsonPropertyName("matched_evidence_excerpt_max_chars")]
    public int MatchedEvidenceExcerptMaxChars { get; init; } = 80;
}

public sealed class DemoTriggerExpectationScript
{
    [JsonPropertyName("script_id")]
    public string ScriptId { get; init; } = string.Empty;

    [JsonPropertyName("trigger_expectations")]
    public IReadOnlyList<DemoTriggerExpectation> TriggerExpectations { get; init; } = [];
}

public sealed class DemoTriggerExpectation
{
    [JsonPropertyName("turn_number")]
    public int TurnNumber { get; init; }

    [JsonPropertyName("speaker")]
    public string Speaker { get; init; } = string.Empty;

    [JsonPropertyName("utterance_excerpt")]
    public string UtteranceExcerpt { get; init; } = string.Empty;

    [JsonPropertyName("expected_cards")]
    public IReadOnlyList<DemoExpectedCard> ExpectedCards { get; init; } = [];
}

public sealed class DemoExpectedCard
{
    [JsonPropertyName("knowledge_item_id")]
    public string KnowledgeItemId { get; init; } = string.Empty;

    [JsonPropertyName("rank")]
    public int Rank { get; init; }

    [JsonPropertyName("matched_evidence")]
    public IReadOnlyList<DemoExpectedEvidence> MatchedEvidence { get; init; } = [];
}

public sealed class DemoExpectedEvidence
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("transcript_text")]
    public string TranscriptText { get; init; } = string.Empty;

    [JsonPropertyName("normalized_text")]
    public string NormalizedText { get; init; } = string.Empty;

    [JsonPropertyName("matched_knowledge_text")]
    public string MatchedKnowledgeText { get; init; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;
}
