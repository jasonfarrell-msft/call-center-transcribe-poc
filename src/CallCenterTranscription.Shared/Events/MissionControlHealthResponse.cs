using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record MissionControlHealthResponse
{
    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; init; } = "unknown";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("isMockFeedActive")]
    public bool IsMockFeedActive { get; init; }

    [JsonPropertyName("acsMediaRoutesLiveReady")]
    public bool AcsMediaRoutesLiveReady { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("components")]
    public IReadOnlyList<MissionControlComponentHealth> Components { get; init; } = [];
}

public sealed record MissionControlComponentHealth
{
    [JsonPropertyName("componentId")]
    public string ComponentId { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "unknown";

    [JsonPropertyName("readiness")]
    public string Readiness { get; init; } = string.Empty;

    [JsonPropertyName("isLive")]
    public bool IsLive { get; init; }

    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = string.Empty;

    [JsonPropertyName("lastCheckedUtc")]
    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
}
