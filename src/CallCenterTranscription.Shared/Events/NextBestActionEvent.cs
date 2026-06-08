using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record NextBestActionEvent : IRealtimeEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "next_best_action";

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

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
