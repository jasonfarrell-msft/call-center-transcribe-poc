using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record NextBestActionEvent : IRealtimeEvent
{
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "next_best_action";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = string.Empty;
}
