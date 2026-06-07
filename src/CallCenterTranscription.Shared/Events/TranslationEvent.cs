using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record TranslationEvent : IRealtimeEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "translation";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("utteranceId")]
    public string UtteranceId { get; init; } = string.Empty;

    [JsonPropertyName("relatedTranscriptEventId")]
    public string RelatedTranscriptEventId { get; init; } = string.Empty;

    [JsonPropertyName("relatedTranscriptSequence")]
    public long? RelatedTranscriptSequence { get; init; }

    [JsonPropertyName("sourceLanguage")]
    public string SourceLanguage { get; init; } = string.Empty;

    [JsonPropertyName("targetLanguage")]
    public string TargetLanguage { get; init; } = "en";

    [JsonPropertyName("originalText")]
    public string OriginalText { get; init; } = string.Empty;

    [JsonPropertyName("translatedText")]
    public string TranslatedText { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
