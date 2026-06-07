using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record SessionCurrentResponse
{
    [JsonPropertyName("call")]
    public CallSessionMetadata Call { get; init; } = new();

    [JsonPropertyName("sentimentSummary")]
    public CallSentimentSummary SentimentSummary { get; init; } = new();

    [JsonPropertyName("isMockFeedActive")]
    public bool IsMockFeedActive { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;
}
