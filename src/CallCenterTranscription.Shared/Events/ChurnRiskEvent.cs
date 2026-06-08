using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record ChurnRiskEvent : IRealtimeEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "churn_risk";

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

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "unknown";

    [JsonPropertyName("riskScore")]
    public double RiskScore { get; init; }

    [JsonPropertyName("rationale")]
    public string Rationale { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
