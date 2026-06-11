using System.Text.Json.Serialization;

namespace CallCenterTranscription.Ai.Knowledge;

public sealed class SyntheticKnowledgeCorpus
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("entries")]
    public IReadOnlyList<SyntheticKnowledgeEntry> Entries { get; init; } = [];
}

public sealed class SyntheticKnowledgeEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("snippet")]
    public string Snippet { get; init; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; init; } = string.Empty;

    [JsonPropertyName("recommendedAction")]
    public string RecommendedAction { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTimeOffset LastUpdatedUtc { get; init; }

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = string.Empty;

    [JsonPropertyName("escalationRequired")]
    public bool EscalationRequired { get; init; }

    [JsonPropertyName("tags")]
    public SyntheticKnowledgeEntryTags Tags { get; init; } = new();
}

public sealed class SyntheticKnowledgeEntryTags
{
    [JsonPropertyName("intents")]
    public IReadOnlyList<string> Intents { get; init; } = [];

    [JsonPropertyName("entities")]
    public IReadOnlyList<string> Entities { get; init; } = [];

    [JsonPropertyName("keywords")]
    public IReadOnlyList<string> Keywords { get; init; } = [];
}
