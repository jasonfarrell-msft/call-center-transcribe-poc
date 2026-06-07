using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record TranscriptEvent : IRealtimeEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "transcript";

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("utteranceId")]
    public string UtteranceId { get; init; } = string.Empty;

    [JsonPropertyName("isFinal")]
    public bool IsFinal { get; init; } = true;

    [JsonPropertyName("speakerId")]
    public string SpeakerId { get; init; } = "unknown";

    [JsonPropertyName("speakerDisplayLabel")]
    public string SpeakerDisplayLabel { get; init; } = "Speaker";

    [JsonPropertyName("speakerRole")]
    public string SpeakerRole { get; init; } = "unknown";

    [JsonPropertyName("speakerLabelSource")]
    public string SpeakerLabelSource { get; init; } = "unknown";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("detectedLanguage")]
    public string? DetectedLanguage { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
