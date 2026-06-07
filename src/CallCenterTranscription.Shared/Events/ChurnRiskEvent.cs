using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record ChurnRiskEvent : IRealtimeEvent
{
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "churn_risk";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("relatedTranscriptSequence")]
    public long? RelatedTranscriptSequence { get; init; }

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "unknown";

    [JsonPropertyName("riskScore")]
    public double RiskScore { get; init; }

    [JsonPropertyName("rationale")]
    public string Rationale { get; init; } = string.Empty;
}
