using CallCenterTranscription.Ai.Knowledge;
using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Ai;

public static class KiraContentPack
{
    private static readonly IReadOnlyList<KiraContentPackEntry> Entries =
        SyntheticCorpusLoader.Corpus.Entries
            .Select(e => new KiraContentPackEntry(
                e.Id,
                e.Title,
                e.Snippet,
                e.SourceUrl,
                e.RecommendedAction,
                e.Tags.Keywords))
            .ToArray();

    public static IReadOnlyList<KiraContentPackEntry> Retrieve(string? transcriptText, int maxCards = 2)
    {
        var normalized = transcriptText?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Entries.Take(maxCards).ToArray();
        }

        return Entries
            .Select(entry => new
            {
                Entry = entry,
                Score = entry.Keywords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal))
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Id, StringComparer.Ordinal)
            .Where(item => item.Score > 0)
            .Select(item => item.Entry)
            .DefaultIfEmpty(Entries[0])
            .Take(maxCards)
            .ToArray();
    }

    public static IReadOnlyList<KnowledgeCard> ToKnowledgeCards(IEnumerable<KiraContentPackEntry> entries) =>
        entries
            .Select(entry => new KnowledgeCard
            {
                Id = entry.Id,
                Title = entry.Title,
                Snippet = entry.Snippet,
                SourceUrl = entry.SourceUrl
            })
            .ToArray();
}

public sealed record KiraContentPackEntry(
    string Id,
    string Title,
    string Snippet,
    string SourceUrl,
    string RecommendedAction,
    IReadOnlyList<string> Keywords);
