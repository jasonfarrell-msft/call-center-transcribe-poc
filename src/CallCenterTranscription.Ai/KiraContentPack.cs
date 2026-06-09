using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Ai;

public static class KiraContentPack
{
    private static readonly IReadOnlyList<KiraContentPackEntry> Entries =
    [
        new(
            "card-retention-price-match",
            "Retention policy: competitor price concerns",
            "Offer a service credit and quote budget billing before discussing cancellation.",
            "https://contoso.example/policies/retention/price-match",
            "Offer immediate service credit and enroll customer in budget billing.",
            ["competitor", "price", "northstar", "lower", "flyer", "volante"]),
        new(
            "card-service-recovery-delivery",
            "Service recovery: missed delivery",
            "Acknowledge the miss, apologize, and confirm expedited delivery before retention offers.",
            "https://contoso.example/policies/service-recovery/missed-delivery",
            "Confirm expedited delivery timing and apply a one-time service recovery credit.",
            ["missed", "delivery", "late", "out of propane", "delivery tonight", "entrega"]),
        new(
            "card-billing-stabilization",
            "Billing stabilization: unexpected bill increase",
            "Use budget billing and clear itemization when a customer reports sharp month-over-month increases.",
            "https://contoso.example/policies/billing/budget-billing",
            "Walk through bill itemization and offer budget billing to stabilize monthly spend.",
            ["bill", "billing", "jumped", "increase", "forty", "budget billing"])
    ];

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
