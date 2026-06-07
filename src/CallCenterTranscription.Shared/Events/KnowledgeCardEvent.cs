using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record KnowledgeCardEvent : IRealtimeEvent
{
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "knowledge_cards";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("cards")]
    public IReadOnlyList<KnowledgeCard> Cards { get; init; } = [];
}

public sealed record KnowledgeCard
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("snippet")]
    public string Snippet { get; init; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }
}
