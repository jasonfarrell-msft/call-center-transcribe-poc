using System.Globalization;
using System.Text;
using CallCenterTranscription.Ai.Knowledge;
using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Ai;

public sealed class DemoAssistMatcher
{
    private static readonly string[] PositiveResolutionSignals =
    [
        "i ll stay",
        "ill stay",
        "go ahead",
        "that works",
        "works for me",
        "renew it",
        "set up the steadier payment plan",
        "switch me to auto delivery",
        "switch me to auto-delivery",
        "i d rather stay",
        "i'd rather stay"
    ];

    private readonly IReadOnlyDictionary<string, AgentAssistKnowledgeItem> _knowledgeById;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<DemoTriggerExpectation>> _expectationsByScriptId;
    private readonly IReadOnlyList<AssistRule> _rules;
    private readonly DemoTriggerRules _triggerRules;

    public DemoAssistMatcher()
        : this(AgentAssistDataLoader.KnowledgeById, AgentAssistDataLoader.ExpectationsByScriptId, AgentAssistDataLoader.TriggerRules)
    {
    }

    public DemoAssistMatcher(
        IReadOnlyDictionary<string, AgentAssistKnowledgeItem> knowledgeById,
        IReadOnlyDictionary<string, IReadOnlyList<DemoTriggerExpectation>> expectationsByScriptId,
        DemoTriggerRules triggerRules)
    {
        _knowledgeById = knowledgeById;
        _expectationsByScriptId = expectationsByScriptId;
        _triggerRules = triggerRules;
        _rules = expectationsByScriptId
            .SelectMany(static pair => pair.Value)
            .SelectMany(expectation => expectation.ExpectedCards.Select(card => new AssistRule(
                expectation.UtteranceExcerpt,
                card.Rank,
                card.KnowledgeItemId,
                card.MatchedEvidence,
                expectation.TurnNumber)))
            .ToArray();
    }

    public DemoAssistMatcher ForScript(string? scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
        {
            return this;
        }

        if (!_expectationsByScriptId.TryGetValue(scriptId, out var expectations))
        {
            return new DemoAssistMatcher(
                _knowledgeById,
                new Dictionary<string, IReadOnlyList<DemoTriggerExpectation>>(StringComparer.OrdinalIgnoreCase),
                _triggerRules);
        }

        return new DemoAssistMatcher(
            _knowledgeById,
            new Dictionary<string, IReadOnlyList<DemoTriggerExpectation>>(StringComparer.OrdinalIgnoreCase)
            {
                [scriptId] = expectations
            },
            _triggerRules);
    }

    public DemoAssistMatchResult Match(TranscriptEvent transcriptEvent, int? maxCards = null)
    {
        if (!IsCustomerTurn(transcriptEvent))
        {
            return DemoAssistMatchResult.Empty(transcriptEvent);
        }

        var normalizedTranscript = Normalize(transcriptEvent.Text);
        if (string.IsNullOrWhiteSpace(normalizedTranscript))
        {
            return DemoAssistMatchResult.Empty(transcriptEvent);
        }

        var targetCardCount = Math.Clamp(maxCards ?? _triggerRules.MaxCardsPerTurn, 1, Math.Max(1, _triggerRules.MaxCardsPerTurn));
        var expectationCandidates = BuildExpectationCandidates(transcriptEvent, normalizedTranscript);
        if (expectationCandidates.Count == 0 && PositiveResolutionSignals.Any(signal => normalizedTranscript.Contains(signal, StringComparison.Ordinal)))
        {
            return DemoAssistMatchResult.Empty(transcriptEvent);
        }

        var candidates = expectationCandidates.Count > 0
            ? expectationCandidates
            : BuildCorpusCandidates(transcriptEvent, normalizedTranscript);

        var selected = candidates
            .GroupBy(static candidate => candidate.KnowledgeItem.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(candidate => candidate.ExpectationRank ?? int.MaxValue)
                .ThenByDescending(static candidate => candidate.Score)
                .First())
            .OrderBy(candidate => candidate.ExpectationRank ?? int.MaxValue)
            .ThenByDescending(static candidate => candidate.Score)
            .ThenByDescending(candidate => PriorityWeight(candidate.KnowledgeItem.Priority))
            .ThenBy(candidate => candidate.KnowledgeItem.Id, StringComparer.Ordinal)
            .Take(targetCardCount)
            .Select((candidate, index) => new DemoAssistCardMatch(candidate.KnowledgeItem, index + 1, candidate.Score, candidate.Evidence))
            .ToArray();

        return selected.Length == 0
            ? DemoAssistMatchResult.Empty(transcriptEvent)
            : new DemoAssistMatchResult(transcriptEvent, selected);
    }

    public static bool IsCustomerTurn(TranscriptEvent transcriptEvent) =>
        string.Equals(transcriptEvent.SpeakerRole, "customer", StringComparison.OrdinalIgnoreCase);

    private List<AssistCandidate> BuildExpectationCandidates(TranscriptEvent transcriptEvent, string normalizedTranscript)
    {
        var candidates = new List<AssistCandidate>();

        foreach (var rule in _rules)
        {
            if (!_knowledgeById.TryGetValue(rule.KnowledgeItemId, out var knowledgeItem))
            {
                continue;
            }

            var score = 0d;
            var evidence = new List<KnowledgeCardMatchedEvidence>();
            if (ContainsNormalized(normalizedTranscript, Normalize(rule.UtteranceExcerpt)))
            {
                score += 100;
            }

            foreach (var expectedEvidence in rule.ExpectedEvidence)
            {
                var transcriptMatch = ContainsNormalized(normalizedTranscript, Normalize(expectedEvidence.TranscriptText));
                var knowledgeMatch = ContainsNormalized(normalizedTranscript, Normalize(expectedEvidence.MatchedKnowledgeText));
                if (!transcriptMatch && !knowledgeMatch)
                {
                    continue;
                }

                score += expectedEvidence.Kind.Contains("keyword", StringComparison.OrdinalIgnoreCase) ? 18 : 32;
                evidence.Add(new KnowledgeCardMatchedEvidence
                {
                    Kind = expectedEvidence.Kind,
                    TranscriptText = TrimToLimit(expectedEvidence.TranscriptText, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    NormalizedText = TrimToLimit(expectedEvidence.NormalizedText, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    MatchedKnowledgeText = TrimToLimit(expectedEvidence.MatchedKnowledgeText, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    Locale = expectedEvidence.Locale
                });
            }

            if (score <= 0 || evidence.Count == 0)
            {
                continue;
            }

            candidates.Add(new AssistCandidate(knowledgeItem, rule.Rank, score, DedupeEvidence(evidence)));
        }

        return candidates;
    }

    private List<AssistCandidate> BuildCorpusCandidates(TranscriptEvent transcriptEvent, string normalizedTranscript)
    {
        var candidates = new List<AssistCandidate>();
        foreach (var knowledgeItem in _knowledgeById.Values)
        {
            var score = 0d;
            var evidence = new List<KnowledgeCardMatchedEvidence>();
            var locale = string.IsNullOrWhiteSpace(transcriptEvent.DetectedLanguage) ? knowledgeItem.Locale : transcriptEvent.DetectedLanguage!;

            foreach (var triggerPhrase in knowledgeItem.TriggerPhrases)
            {
                if (!ContainsNormalized(normalizedTranscript, Normalize(triggerPhrase)))
                {
                    continue;
                }

                score += 18;
                evidence.Add(new KnowledgeCardMatchedEvidence
                {
                    Kind = "trigger_phrase",
                    TranscriptText = TrimToLimit(triggerPhrase, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    NormalizedText = TrimToLimit(triggerPhrase, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    MatchedKnowledgeText = TrimToLimit(triggerPhrase, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    Locale = locale
                });
            }

            foreach (var keyword in knowledgeItem.Keywords)
            {
                if (!ContainsNormalized(normalizedTranscript, Normalize(keyword)))
                {
                    continue;
                }

                score += 10;
                evidence.Add(new KnowledgeCardMatchedEvidence
                {
                    Kind = "keyword",
                    TranscriptText = TrimToLimit(keyword, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    NormalizedText = TrimToLimit(keyword, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    MatchedKnowledgeText = TrimToLimit(keyword, _triggerRules.MatchedEvidenceExcerptMaxChars),
                    Locale = locale
                });
            }

            if (score <= 0 || evidence.Count == 0)
            {
                continue;
            }

            candidates.Add(new AssistCandidate(knowledgeItem, null, score, DedupeEvidence(evidence)));
        }

        return candidates;
    }

    private static IReadOnlyList<KnowledgeCardMatchedEvidence> DedupeEvidence(IEnumerable<KnowledgeCardMatchedEvidence> evidence) =>
        evidence
            .GroupBy(item => $"{item.Kind}|{item.TranscriptText}|{item.MatchedKnowledgeText}|{item.Locale}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(3)
            .ToArray();

    private static bool ContainsNormalized(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(needle) && haystack.Contains(needle, StringComparison.Ordinal);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var current = char.ToLowerInvariant(ch);
            if (char.IsLetterOrDigit(current))
            {
                builder.Append(current);
                previousWasSpace = false;
                continue;
            }

            if (previousWasSpace)
            {
                continue;
            }

            builder.Append(' ');
            previousWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    private static string TrimToLimit(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars].TrimEnd();
    }

    private static int PriorityWeight(string? priority) => priority?.Trim().ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "normal" => 1,
        _ => 0
    };

    private sealed record AssistRule(
        string UtteranceExcerpt,
        int Rank,
        string KnowledgeItemId,
        IReadOnlyList<DemoExpectedEvidence> ExpectedEvidence,
        int TurnNumber);

    private sealed record AssistCandidate(
        AgentAssistKnowledgeItem KnowledgeItem,
        int? ExpectationRank,
        double Score,
        IReadOnlyList<KnowledgeCardMatchedEvidence> Evidence);
}

public sealed record DemoAssistCardMatch(
    AgentAssistKnowledgeItem KnowledgeItem,
    int Rank,
    double Score,
    IReadOnlyList<KnowledgeCardMatchedEvidence> Evidence)
{
    public KnowledgeCard ToKnowledgeCard() => new()
    {
        Id = KnowledgeItem.Id,
        Title = KnowledgeItem.Title,
        Snippet = string.IsNullOrWhiteSpace(KnowledgeItem.RepGuidance) ? KnowledgeItem.Answer : KnowledgeItem.RepGuidance,
        SourceUrl = KnowledgeItem.SourceUri,
        CitationLabel = KnowledgeItem.CitationLabel,
        SourceSection = KnowledgeItem.SourceSection,
        Rank = Rank,
        MatchedEvidence = Evidence
    };
}

public sealed class DemoAssistMatchResult
{
    private static readonly string[] PositiveResolutionSignals =
    [
        "i ll stay",
        "ill stay",
        "go ahead",
        "that works",
        "works for me",
        "renew it",
        "set up the steadier payment plan",
        "switch me to auto delivery",
        "switch me to auto-delivery",
        "i d rather stay",
        "i'd rather stay"
    ];

    internal DemoAssistMatchResult(TranscriptEvent transcriptEvent, IReadOnlyList<DemoAssistCardMatch> matches)
    {
        TranscriptEvent = transcriptEvent;
        Matches = matches;
    }

    public TranscriptEvent TranscriptEvent { get; }
    public IReadOnlyList<DemoAssistCardMatch> Matches { get; }
    public bool HasMatches => Matches.Count > 0;
    public DemoAssistCardMatch? PrimaryMatch => Matches.FirstOrDefault();

    public static DemoAssistMatchResult Empty(TranscriptEvent transcriptEvent) => new(transcriptEvent, []);

    public KnowledgeCardEvent CreateKnowledgeCardEvent(string source, DateTimeOffset timestampUtc) => new()
    {
        CallId = TranscriptEvent.CallId,
        EventId = $"evt-knowledge-card-assist-{TranscriptEvent.Sequence}",
        Sequence = TranscriptEvent.Sequence,
        UtteranceId = TranscriptEvent.UtteranceId,
        RelatedTranscriptEventId = TranscriptEvent.EventId,
        RelatedTranscriptSequence = TranscriptEvent.Sequence,
        TimestampUtc = timestampUtc,
        Cards = Matches.Select(static match => match.ToKnowledgeCard()).ToArray(),
        Source = source
    };

    public NextBestActionEvent CreateNextBestActionEvent(string source, DateTimeOffset timestampUtc)
    {
        var primary = PrimaryMatch ?? throw new InvalidOperationException("Cannot create next-best-action event without at least one assist match.");
        return new NextBestActionEvent
        {
            CallId = TranscriptEvent.CallId,
            EventId = $"evt-nba-assist-{TranscriptEvent.Sequence}",
            Sequence = TranscriptEvent.Sequence,
            UtteranceId = TranscriptEvent.UtteranceId,
            RelatedTranscriptEventId = TranscriptEvent.EventId,
            RelatedTranscriptSequence = TranscriptEvent.Sequence,
            TimestampUtc = timestampUtc,
            Action = primary.KnowledgeItem.NextBestAction,
            Confidence = Math.Clamp(0.55 + (primary.Score / 120d), 0.55, 0.97),
            Reasoning = $"Matched {string.Join("; ", Matches.Select(match => match.KnowledgeItem.Title))} from the customer turn.",
            Source = source
        };
    }

    public ChurnRiskEvent CreateChurnRiskEvent(string source, DateTimeOffset timestampUtc)
    {
        var titles = string.Join("; ", Matches.Select(match => match.KnowledgeItem.Title));
        var normalizedTranscript = TranscriptEvent.Text.ToLowerInvariant();
        var baseScore = PrimaryMatch is null
            ? 0.4
            : PrimaryMatch.KnowledgeItem.Priority.Trim().ToLowerInvariant() switch
            {
                "critical" => 0.92,
                "high" => 0.82,
                "medium" => 0.66,
                "normal" => 0.52,
                _ => 0.4
            };

        if (PositiveResolutionSignals.Any(signal => normalizedTranscript.Contains(signal, StringComparison.Ordinal)))
        {
            baseScore -= 0.28;
        }
        else if (normalizedTranscript.Contains("switch", StringComparison.Ordinal) ||
                 normalizedTranscript.Contains("leave", StringComparison.Ordinal) ||
                 normalizedTranscript.Contains("cambiarme", StringComparison.Ordinal))
        {
            baseScore += 0.08;
        }

        var riskScore = Math.Clamp(baseScore, 0.1, 0.95);
        var riskLevel = riskScore switch
        {
            >= 0.75 => "high",
            >= 0.45 => "moderate",
            _ => "low"
        };

        return new ChurnRiskEvent
        {
            CallId = TranscriptEvent.CallId,
            EventId = $"evt-churn-risk-assist-{TranscriptEvent.Sequence}",
            Sequence = TranscriptEvent.Sequence,
            UtteranceId = TranscriptEvent.UtteranceId,
            RelatedTranscriptEventId = TranscriptEvent.EventId,
            RelatedTranscriptSequence = TranscriptEvent.Sequence,
            TimestampUtc = timestampUtc,
            RiskLevel = riskLevel,
            RiskScore = riskScore,
            Rationale = $"Customer turn matched {titles}.",
            Source = source
        };
    }
}
