using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

/// <summary>
/// Broadcast to all console clients when an ACS call starts or ends. Carries the
/// ACS-generated call connection ID so the browser can subscribe to the correct
/// SignalR group ("call:{callId}") and drive its connection-state UI.
/// </summary>
public sealed record CallLifecycleEvent
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    /// <summary>"started" or "ended".</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
