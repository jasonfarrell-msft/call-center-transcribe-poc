using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record SentimentEvent : IRealtimeEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "sentiment";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("utteranceId")]
    public string? UtteranceId { get; init; }

    [JsonPropertyName("relatedTranscriptEventId")]
    public string? RelatedTranscriptEventId { get; init; }

    [JsonPropertyName("relatedTranscriptSequence")]
    public long? RelatedTranscriptSequence { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = "neutral";

    [JsonPropertyName("trend")]
    public string Trend { get; init; } = "steady";

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
