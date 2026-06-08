using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record PipelineCurrentStateResponse
{
    [JsonPropertyName("call")]
    public CallSessionMetadata Call { get; init; } = new();

    [JsonPropertyName("sentimentSummary")]
    public CallSentimentSummary SentimentSummary { get; init; } = new();

    [JsonPropertyName("isMockFeedActive")]
    public bool IsMockFeedActive { get; init; }

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("streamReplayPolicy")]
    public string StreamReplayPolicy { get; init; } = "full_history_for_active_call";

    [JsonPropertyName("transcriptEvents")]
    public IReadOnlyList<TranscriptEvent> TranscriptEvents { get; init; } = [];

    [JsonPropertyName("translationEvents")]
    public IReadOnlyList<TranslationEvent> TranslationEvents { get; init; } = [];

    [JsonPropertyName("sentimentEvents")]
    public IReadOnlyList<SentimentEvent> SentimentEvents { get; init; } = [];

    [JsonPropertyName("churnRiskEvents")]
    public IReadOnlyList<ChurnRiskEvent> ChurnRiskEvents { get; init; } = [];

    [JsonPropertyName("knowledgeCardEvents")]
    public IReadOnlyList<KnowledgeCardEvent> KnowledgeCardEvents { get; init; } = [];

    [JsonPropertyName("nextBestActionEvents")]
    public IReadOnlyList<NextBestActionEvent> NextBestActionEvents { get; init; } = [];
}
