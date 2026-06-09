using System.Text.RegularExpressions;

namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Lightweight, deterministic lexicon sentiment scorer for the live call panel.
///
/// POC rationale: the rep console needs a sentiment signal that visibly tracks the live
/// conversation without standing up an additional Azure AI Foundry chat deployment for the
/// POC. This heuristic scores each finalized utterance in [-1, 1] from a curated
/// positive/negative word list, with simple negation ("not happy") and intensifier
/// ("very angry") handling. It is intentionally explainable and side-effect free; swapping in
/// an Azure AI Foundry model later only requires replacing <see cref="Score"/>.
/// </summary>
public static partial class SentimentLexicon
{
    private static readonly HashSet<string> Positive = new(StringComparer.OrdinalIgnoreCase)
    {
        "thanks", "thank", "thankyou", "great", "good", "appreciate", "appreciated", "happy",
        "glad", "perfect", "excellent", "wonderful", "awesome", "love", "helpful", "resolved",
        "resolve", "fixed", "fix", "yes", "agree", "agreed", "fair", "reasonable", "okay", "ok",
        "fine", "satisfied", "pleased", "smooth", "easy", "kind", "wonderful", "nice", "better",
        "understand", "understood", "sure", "absolutely", "definitely", "works", "working",
    };

    private static readonly HashSet<string> Negative = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "not", "never", "angry", "upset", "frustrated", "frustrating", "frustration",
        "annoyed", "annoying", "disappointed", "disappointing", "unhappy", "terrible", "awful",
        "horrible", "bad", "worse", "worst", "cancel", "cancelled", "cancelling", "leave",
        "leaving", "switch", "switching", "complaint", "complain", "problem", "problems", "issue",
        "issues", "broken", "wrong", "late", "delay", "delayed", "overcharged", "overcharge",
        "charge", "expensive", "ridiculous", "unacceptable", "refund", "fail", "failed", "failure",
        "wait", "waiting", "useless", "rude", "ignore", "ignored", "mistake", "error", "hate",
        "disgusted", "furious", "scam", "lied", "lie", "mad", "stupid", "nonsense",
    };

    private static readonly HashSet<string> Negators = new(StringComparer.OrdinalIgnoreCase)
    {
        "not", "no", "never", "without", "cant", "cannot", "dont", "doesnt", "didnt", "wont",
        "isnt", "arent", "wasnt", "werent", "havent", "hasnt", "wouldnt", "couldnt", "shouldnt",
    };

    private static readonly HashSet<string> Intensifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "very", "really", "so", "extremely", "absolutely", "totally", "completely", "incredibly",
        "super", "quite",
    };

    [GeneratedRegex(@"[a-zA-Z']+")]
    private static partial Regex WordRegex();

    /// <summary>
    /// Scores a single utterance in [-1, 1]. Returns 0 when no sentiment-bearing words are found
    /// (so silence/neutral chatter does not skew the rolling average).
    /// </summary>
    public static double Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0d;
        }

        var tokens = WordRegex().Matches(text)
            .Select(m => m.Value.Replace("'", string.Empty))
            .ToArray();

        if (tokens.Length == 0)
        {
            return 0d;
        }

        double total = 0d;
        var hits = 0;

        for (var i = 0; i < tokens.Length; i++)
        {
            var word = tokens[i];

            double value;
            if (Positive.Contains(word))
            {
                value = 1d;
            }
            else if (Negative.Contains(word))
            {
                value = -1d;
            }
            else
            {
                continue;
            }

            // Look back up to two tokens for a negator or intensifier.
            var multiplier = 1d;
            for (var back = 1; back <= 2 && i - back >= 0; back++)
            {
                var prior = tokens[i - back];
                if (Negators.Contains(prior))
                {
                    multiplier *= -1d;
                }
                else if (Intensifiers.Contains(prior))
                {
                    multiplier *= 1.5d;
                }
            }

            total += value * multiplier;
            hits++;
        }

        if (hits == 0)
        {
            return 0d;
        }

        // Normalize by hit count then squash so a couple of strong words don't peg the meter.
        var average = total / hits;
        return Math.Clamp(Math.Tanh(average), -1d, 1d);
    }
}
