namespace CallCenterTranscription.Api.Services;

public sealed class ReasoningOptions
{
    public string Mode { get; init; } = "Mock";
    public bool FallbackToMock { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 12;
    public int MaxKnowledgeCards { get; init; } = 2;
    public string FoundryChatCompletionsUrl { get; init; } = string.Empty;
    public string FoundryModel { get; init; } = string.Empty;
    public string FoundryApiVersion { get; init; } = "2024-06-01";
    public string FoundryAudience { get; init; } = "https://cognitiveservices.azure.com/.default";
}
