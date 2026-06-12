using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record ActiveCallStateResponse
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = "idle";

    [JsonPropertyName("acceptAvailable")]
    public bool AcceptAvailable { get; init; }

    [JsonPropertyName("repAccepted")]
    public bool RepAccepted { get; init; }

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset? StartedAtUtc { get; init; }
}
