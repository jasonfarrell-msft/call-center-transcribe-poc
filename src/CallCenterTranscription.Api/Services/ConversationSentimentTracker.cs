using System.Text;

namespace CallCenterTranscription.Api.Services;

internal sealed class ConversationSentimentTracker
{
    private const int CustomerWindowSize = 2;
    private const double ResolutionOfferBoost = 0.35d;
    private const double UpsellOfferBoost = 0.10d;
    private const double ResolutionAcceptanceBoost = 0.35d;
    private const double UpsellAcceptanceBoost = 0.15d;

    private static readonly string[] StrongAcceptancePhrases =
    [
        "ill stay",
        "i ll stay",
        "go ahead and set up",
        "go ahead and switch",
        "works for me",
        "renew it",
        "rather stay",
        "switch me to auto delivery",
        "switch me to auto-delivery",
        "set up the steadier payment plan",
        "split the balance the way you described"
    ];

    private static readonly string[] ExitPhrases =
    [
        "cancel my account",
        "go ahead and cancel",
        "leave valley fuel",
        "switch providers",
        "move the account"
    ];

    private static readonly string[] UpsellTerms =
    [
        "auto delivery",
        "auto-delivery",
        "keep full",
        "keep-full",
        "budget billing",
        "payment plan",
        "loyalty option"
    ];

    private static readonly string[] CustomerRiskPhrases =
    [
        "run out",
        "thinking about switching",
        "thinking about leaving",
        "may need to leave",
        "cannot pay",
        "cant pay",
        "missed",
        "delay",
        "delayed"
    ];

    private static readonly string[] ExploratoryResolutionPhrases =
    [
        "whats the difference",
        "what s the difference",
        "difference between",
        "how does",
        "set me up",
        "set it up"
    ];

    private static readonly string[] ResolutionActionPhrases =
    [
        "i can",
        "we can",
        "ill",
        "i ll",
        "well",
        "we ll",
        "lets",
        "let s"
    ];

    private static readonly string[] ResolutionTargetPhrases =
    [
        "schedule",
        "delivery",
        "replacement",
        "window",
        "payment plan",
        "budget billing",
        "renew",
        "renewal",
        "loyalty option",
        "auto delivery",
        "auto-delivery",
        "keep full",
        "keep-full",
        "update"
    ];

    private readonly Queue<double> _recentCustomerSignals = new();

    private bool _resolutionOffered;
    private bool _upsellOffered;
    private bool _customerAccepted;
    private bool _upsellAccepted;
    private double? _previousPublishedScore;

    public string OverallLabel { get; private set; } = "neutral";
    public string Trend { get; private set; } = "steady";
    public string SummaryText { get; private set; } = string.Empty;
    public double CurrentScore { get; private set; }

    public void Reset()
    {
        _recentCustomerSignals.Clear();
        _resolutionOffered = false;
        _upsellOffered = false;
        _customerAccepted = false;
        _upsellAccepted = false;
        _previousPublishedScore = null;
        OverallLabel = "neutral";
        Trend = "steady";
        SummaryText = string.Empty;
        CurrentScore = 0d;
    }

    public SentimentAssessment? ObserveTurn(string speakerRole, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return null;
        }

        var isRep = string.Equals(speakerRole, "rep", StringComparison.OrdinalIgnoreCase);
        if (isRep)
        {
            var repRelevant = ObserveRepTurn(normalized);
            if (!repRelevant || _recentCustomerSignals.Count == 0)
            {
                return null;
            }
        }
        else
        {
            var customerSignal = BuildCustomerSignal(text, normalized);
            if (Math.Abs(customerSignal) < 0.001d)
            {
                return null;
            }

            PushCustomerSignal(customerSignal);
            ObserveCustomerOutcome(normalized);
        }

        var score = ComputeScore();
        var trend = ResolveTrend(_previousPublishedScore, score);
        var eventLabel = ResolveScoreLabel(score);
        var overallLabel = ResolveCompositeLabel(score);
        var summaryText = ResolveSummaryText(score, overallLabel);

        _previousPublishedScore = score;
        CurrentScore = score;
        Trend = trend;
        OverallLabel = overallLabel;
        SummaryText = summaryText;

        return new SentimentAssessment(score, eventLabel, overallLabel, trend, summaryText);
    }

    private double BuildCustomerSignal(string text, string normalized)
    {
        var signal = SentimentLexicon.Score(text);

        if (ContainsAny(normalized, CustomerRiskPhrases))
        {
            signal -= 0.18d;
        }

        if (ContainsAny(normalized, ExploratoryResolutionPhrases))
        {
            signal += 0.12d;
        }

        if (!ContainsAny(normalized, ExitPhrases) && ContainsAny(normalized, StrongAcceptancePhrases))
        {
            signal += 0.55d;
        }

        return Math.Clamp(signal, -1d, 1d);
    }

    private void ObserveCustomerOutcome(string normalized)
    {
        if (!ContainsAny(normalized, ExitPhrases) && ContainsAny(normalized, StrongAcceptancePhrases))
        {
            _customerAccepted = true;
        }

        if (_customerAccepted && ContainsAny(normalized, UpsellTerms))
        {
            _upsellAccepted = true;
        }
    }

    private bool ObserveRepTurn(string normalized)
    {
        var mentionsUpsell = ContainsAny(normalized, UpsellTerms);
        var offersConcreteResolution =
            ContainsAny(normalized, ResolutionActionPhrases) &&
            ContainsAny(normalized, ResolutionTargetPhrases);

        if (offersConcreteResolution)
        {
            _resolutionOffered = true;
        }

        if (mentionsUpsell)
        {
            _upsellOffered = true;
        }

        return offersConcreteResolution || mentionsUpsell;
    }

    private double ComputeScore()
    {
        var baseScore = _recentCustomerSignals.Count == 0 ? 0d : _recentCustomerSignals.Average();

        if (_resolutionOffered)
        {
            baseScore += ResolutionOfferBoost;
        }

        if (_upsellOffered)
        {
            baseScore += UpsellOfferBoost;
        }

        if (_customerAccepted)
        {
            baseScore += ResolutionAcceptanceBoost;
        }

        if (_upsellAccepted)
        {
            baseScore += UpsellAcceptanceBoost;
        }

        return Math.Clamp(baseScore, -1d, 1d);
    }

    private static string ResolveBaseLabel(double score)
    {
        if (score >= 0.25d)
        {
            return "positive";
        }

        if (score <= -0.25d)
        {
            return "negative";
        }

        return "neutral";
    }

    private string ResolveCompositeLabel(double score)
    {
        if (_customerAccepted && _resolutionOffered)
        {
            return "resolved";
        }

        if (_resolutionOffered && score > -0.25d)
        {
            return "improving";
        }

        return ResolveBaseLabel(score);
    }

    private string ResolveSummaryText(double score, string overallLabel)
    {
        if (string.Equals(overallLabel, "resolved", StringComparison.Ordinal))
        {
            return _upsellAccepted
                ? "Customer accepted the fix and converted to the recommended plan."
                : "Customer accepted the fix and agreed to stay.";
        }

        if (_resolutionOffered && score > -0.25d)
        {
            return "Rep has offered a concrete next step and the tone is recovering.";
        }

        return string.Empty;
    }

    private void PushCustomerSignal(double signal)
    {
        _recentCustomerSignals.Enqueue(signal);
        while (_recentCustomerSignals.Count > CustomerWindowSize)
        {
            _recentCustomerSignals.Dequeue();
        }
    }

    private static bool ContainsAny(string normalized, IEnumerable<string> phrases) =>
        phrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));

    private static string ResolveScoreLabel(double score) => score switch
    {
        <= -0.25d => "negative",
        < 0.25d => "neutral",
        _ => "positive",
    };

    private static string ResolveTrend(double? previous, double current)
    {
        if (previous is null)
        {
            return "steady";
        }

        var delta = current - previous.Value;
        return delta switch
        {
            > 0.05d => "improving",
            < -0.05d => "declining",
            _ => "steady",
        };
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

internal sealed record SentimentAssessment(
    double Score,
    string EventLabel,
    string OverallLabel,
    string Trend,
    string SummaryText);
