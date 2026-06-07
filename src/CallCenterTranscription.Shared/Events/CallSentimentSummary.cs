using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record CallSentimentSummary
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("overallLabel")]
    public string OverallLabel { get; init; } = "neutral";

    [JsonPropertyName("trend")]
    public string Trend { get; init; } = "steady";

    [JsonPropertyName("summaryText")]
    public string SummaryText { get; init; } = string.Empty;

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
