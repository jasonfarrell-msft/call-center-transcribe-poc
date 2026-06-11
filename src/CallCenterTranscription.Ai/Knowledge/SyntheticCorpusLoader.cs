using System.Text.Json;

namespace CallCenterTranscription.Ai.Knowledge;

public static class SyntheticCorpusLoader
{
    private static readonly Lazy<SyntheticKnowledgeCorpus> LazyCorpus = new(Load, isThreadSafe: true);

    public static SyntheticKnowledgeCorpus Corpus => LazyCorpus.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static SyntheticKnowledgeCorpus Load()
    {
        var assembly = typeof(SyntheticCorpusLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("synthetic-knowledge.v1.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Embedded resource 'synthetic-knowledge.v1.json' not found. " +
                "Ensure the file is marked as EmbeddedResource in the .csproj.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Could not open embedded resource stream for '{resourceName}'.");

        return JsonSerializer.Deserialize<SyntheticKnowledgeCorpus>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                "Failed to deserialize synthetic knowledge corpus from embedded resource.");
    }
}
