using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public interface IRealtimeEvent
{
    [JsonPropertyName("eventType")]
    string EventType { get; }

    [JsonPropertyName("timestampUtc")]
    DateTimeOffset TimestampUtc { get; }
}
