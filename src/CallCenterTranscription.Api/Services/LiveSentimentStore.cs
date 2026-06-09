using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Singleton that turns the live transcript into a rolling customer-sentiment signal for the
/// rep console's sentiment panel. Replaces the previously hard-coded scripted score in ACS
/// (live) mode so the meter actually moves with the conversation.
///
/// Flow: <see cref="SpeechTranscriptionService"/> calls <see cref="Append"/> on every finalized
/// utterance. Each utterance is scored with <see cref="SentimentLexicon"/> and folded into an
/// exponentially-weighted rolling score (recent speech weighted highest), so the panel — which
/// the browser re-polls every few seconds — tracks the call in near real time.
///
/// POC constraints (maxReplicas=1, single concurrent call): in-memory, no distributed state.
/// <see cref="Reset"/> is called when a call's media stream begins and <see cref="Clear"/> when
/// it ends, so each call starts from a clean "Waiting for sentiment" state.
/// </summary>
public sealed class LiveSentimentStore
{
    // Smoothing factor for the exponential moving average. Higher == more reactive to the
    // latest utterance; lower == steadier. 0.4 lets the meter move with the conversation while
    // resisting single-word whiplash.
    private const double SmoothingAlpha = 0.4d;

    // Cap the per-call event history so a long call cannot grow memory unbounded; the panel
    // only needs the most recent points for trend.
    private const int MaxEvents = 50;

    private readonly object _gate = new();
    private readonly List<SentimentEvent> _events = new();

    private bool _active;
    private string? _callId;
    private double? _rollingScore;
    private double? _previousRollingScore;
    private long _sequence;

    /// <summary>Starts a fresh sentiment session for a newly connected call.</summary>
    public void Reset(string? callId)
    {
        lock (_gate)
        {
            _active = true;
            _callId = callId;
            _rollingScore = null;
            _previousRollingScore = null;
            _sequence = 0;
            _events.Clear();
        }
    }

    /// <summary>Clears all live sentiment state when a call ends.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _active = false;
            _callId = null;
            _rollingScore = null;
            _previousRollingScore = null;
            _sequence = 0;
            _events.Clear();
        }
    }

    /// <summary>
    /// Scores a finalized utterance and folds it into the rolling sentiment for the call.
    /// Utterances with no sentiment-bearing words (score 0) are ignored so neutral chatter and
    /// silence do not drag the meter toward the midpoint.
    /// </summary>
    public void Append(string callId, string? text)
    {
        var utteranceScore = SentimentLexicon.Score(text);
        if (utteranceScore == 0d)
        {
            return;
        }

        lock (_gate)
        {
            // Only accept utterances while a call is actively being tracked. After Clear()
            // (between calls) the session is inactive, so late utterances delivered by the
            // Speech SDK after the media stream closed are dropped and never leak into the
            // next call. (Reviewer fix: closes the call-teardown race.)
            if (!_active)
            {
                return;
            }

            if (string.IsNullOrEmpty(_callId))
            {
                // Reset ran before the call id was known; adopt the first utterance's id.
                _callId = callId;
            }
            else if (!string.Equals(_callId, callId, StringComparison.Ordinal))
            {
                // Late utterance from a different (prior) call — drop it.
                return;
            }

            _previousRollingScore = _rollingScore;
            _rollingScore = _rollingScore is null
                ? utteranceScore
                : (SmoothingAlpha * utteranceScore) + ((1d - SmoothingAlpha) * _rollingScore.Value);

            var score = Math.Clamp(_rollingScore.Value, -1d, 1d);
            var trend = ResolveTrend(_previousRollingScore, _rollingScore);

            _events.Add(new SentimentEvent
            {
                CallId = callId,
                EventId = $"evt-sentiment-live-{++_sequence}",
                TimestampUtc = DateTimeOffset.UtcNow,
                Label = ResolveLabel(score),
                Trend = trend,
                Score = score,
                Source = "live-lexicon",
            });

            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }
    }

    /// <summary>
    /// Returns the current rolling sentiment as a <see cref="SentimentFeedResponse"/>. When no
    /// scored utterance has arrived yet the feed is empty, which the console renders as
    /// "Waiting for sentiment." The summary text is intentionally left blank in live mode.
    /// </summary>
    public SentimentFeedResponse GetFeed()
    {
        lock (_gate)
        {
            if (_rollingScore is null || _events.Count == 0)
            {
                return new SentimentFeedResponse();
            }

            var score = Math.Clamp(_rollingScore.Value, -1d, 1d);
            var latest = _events[^1];

            return new SentimentFeedResponse
            {
                CallId = _callId ?? string.Empty,
                Summary = new CallSentimentSummary
                {
                    CallId = _callId ?? string.Empty,
                    OverallLabel = ResolveLabel(score),
                    Trend = latest.Trend,
                    SummaryText = string.Empty,
                    UpdatedAtUtc = latest.TimestampUtc,
                    Source = "live-lexicon",
                },
                Events = _events.ToArray(),
            };
        }
    }

    private static string ResolveLabel(double score) => score switch
    {
        <= -0.2d => "negative",
        < 0.2d => "neutral",
        _ => "positive",
    };

    private static string ResolveTrend(double? previous, double? current)
    {
        if (previous is null || current is null)
        {
            return "steady";
        }

        var delta = current.Value - previous.Value;
        return delta switch
        {
            > 0.05d => "improving",
            < -0.05d => "declining",
            _ => "steady",
        };
    }
}
