using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record SentimentFeedResponse
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public CallSentimentSummary Summary { get; init; } = new();

    [JsonPropertyName("events")]
    public IReadOnlyList<SentimentEvent> Events { get; init; } = [];
}
