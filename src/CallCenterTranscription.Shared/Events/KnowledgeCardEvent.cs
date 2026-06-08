using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record KnowledgeCardEvent : IRealtimeEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "knowledge_cards";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("utteranceId")]
    public string? UtteranceId { get; init; }

    [JsonPropertyName("relatedTranscriptEventId")]
    public string? RelatedTranscriptEventId { get; init; }

    [JsonPropertyName("relatedTranscriptSequence")]
    public long? RelatedTranscriptSequence { get; init; }

    [JsonPropertyName("cards")]
    public IReadOnlyList<KnowledgeCard> Cards { get; init; } = [];

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
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
