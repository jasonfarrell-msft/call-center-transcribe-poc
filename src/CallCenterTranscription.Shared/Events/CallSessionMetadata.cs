using System.Text.Json.Serialization;

namespace CallCenterTranscription.Shared.Events;

public sealed record CallSessionMetadata
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("customerAccountId")]
    public string CustomerAccountId { get; init; } = string.Empty;

    [JsonPropertyName("agentName")]
    public string AgentName { get; init; } = string.Empty;

    [JsonPropertyName("queueName")]
    public string QueueName { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = "active";

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("scenarioName")]
    public string ScenarioName { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "unknown";
}
