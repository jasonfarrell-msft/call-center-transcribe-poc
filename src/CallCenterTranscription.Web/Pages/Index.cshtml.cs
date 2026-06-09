using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace CallCenterTranscription.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly PipelineApiClient _pipelineApiClient;
    private readonly List<string> _connectionIssues = [];

    public IndexModel(
        ILogger<IndexModel> logger,
        PipelineApiClient pipelineApiClient,
        IOptions<BackendApiOptions> backendApiOptions,
        IConfiguration configuration)
    {
        _logger = logger;
        _pipelineApiClient = pipelineApiClient;
        ApiBaseUrl = (backendApiOptions.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        LiveMode = configuration.GetValue<bool>("Frontend:LiveMode");
    }

    /// <summary>When true, the transcript column is driven by the live SignalR stream instead of
    /// the scripted feed. Exposed to the browser via data attributes for live-transcript.js.</summary>
    public bool LiveMode { get; }

    /// <summary>Public base URL of the backend API, used by the browser SignalR client.</summary>
    public string ApiBaseUrl { get; }

    public SessionCurrentResponse CurrentSession { get; private set; } = new();
    public SentimentFeedResponse SentimentFeed { get; private set; } = new();
    public MissionControlHealthResponse MissionControlHealth { get; private set; } = new();
    public IReadOnlyList<TranscriptTimelineItem> TranscriptTimeline { get; private set; } = [];
    public string ConnectionSummary { get; private set; } = "Backend API disconnected";
    public string? SessionWarning { get; private set; }
    public string? TranscriptWarning { get; private set; }
    public string? TranslationWarning { get; private set; }
    public string? SentimentWarning { get; private set; }
    public string? MissionControlWarning { get; private set; }
    public bool HasConnectionIssues => _connectionIssues.Count > 0;
    public IReadOnlyList<string> ConnectionIssues => _connectionIssues;
    public bool HasActiveCall => !string.IsNullOrWhiteSpace(CurrentSession.Call.CallId);
    public string RepresentativeDisplayName => string.IsNullOrWhiteSpace(CurrentSession.Call.AgentName)
        ? "Representative"
        : CurrentSession.Call.AgentName;
    public string CallIdDisplay => HasActiveCall ? CurrentSession.Call.CallId : "Waiting for call";
    public string CustomerDisplayName => HasActiveCall && !string.IsNullOrWhiteSpace(CurrentSession.Call.CustomerName)
        ? CurrentSession.Call.CustomerName
        : "Waiting for call";
    public string ConnectionTimeDisplay => HasActiveCall
        ? CurrentSession.Call.StartedAtUtc.ToLocalTime().ToString("h:mm tt")
        : "Waiting for call";
    public string FeedModeLabel => CurrentSession.IsMockFeedActive ? "Mock feed" : "Live feed";
    public SentimentPresentation Sentiment { get; private set; } = SentimentPresentation.Waiting;

    public async Task OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;

        // In live mode the header and transcript are driven entirely by the live SignalR
        // connection-state machine (client-side). We deliberately skip the scripted
        // /api/session/current mock feed so the top bar never leaks stale mock customer,
        // "Mock feed active" or connected-timestamp metadata that would desync from the
        // real connection state shown in the transcript column.
        var currentSessionTask = LiveMode
            ? null
            : _pipelineApiClient.GetCurrentSessionAsync(cancellationToken);
        var transcriptTask = _pipelineApiClient.GetTranscriptEventsAsync(cancellationToken);
        var translationTask = _pipelineApiClient.GetTranslationEventsAsync(cancellationToken);
        var sentimentTask = _pipelineApiClient.GetSentimentFeedAsync(cancellationToken);
        var missionControlTask = _pipelineApiClient.GetMissionControlHealthAsync(cancellationToken);

        await Task.WhenAll(transcriptTask, translationTask, sentimentTask, missionControlTask);

        var transcriptResult = await transcriptTask;
        var translationResult = await translationTask;
        var sentimentResult = await sentimentTask;
        var missionControlResult = await missionControlTask;

        if (currentSessionTask is not null)
        {
            var currentSessionResult = await currentSessionTask;
            CurrentSession = ResolveResult(
                currentSessionResult,
                new SessionCurrentResponse(),
                "session context",
                warning => SessionWarning = warning);
        }
        else
        {
            // Live mode: render neutral header placeholders; live-transcript.js updates them
            // (Call ID, customer, connected time, status summary) on callStarted/callEnded.
            CurrentSession = new SessionCurrentResponse
            {
                Call = new CallSessionMetadata
                {
                    State = "waiting",
                    Source = "acs-live"
                },
                IsMockFeedActive = false,
                Notes = "Live customer-to-representative interaction is enabled and waiting for a call."
            };
        }

        var transcriptEvents = ResolveResult(
            transcriptResult,
            [],
            "transcript feed",
            warning => TranscriptWarning = warning);
        var translationEvents = ResolveResult(
            translationResult,
            [],
            "translation feed",
            warning => TranslationWarning = warning);
        SentimentFeed = ResolveResult(
            sentimentResult,
            new SentimentFeedResponse(),
            "sentiment feed",
            warning => SentimentWarning = warning);
        MissionControlHealth = ResolveResult(
            missionControlResult,
            new MissionControlHealthResponse(),
            "mission control feed",
            warning => MissionControlWarning = warning);

        TranscriptTimeline = BuildTranscriptTimeline(transcriptEvents, translationEvents);
        Sentiment = BuildSentimentPresentation(SentimentFeed);
        ConnectionSummary = currentSessionTask is null
            ? "Live mode • Waiting for call"
            : BuildConnectionSummary();
    }

    private static IReadOnlyList<TranscriptTimelineItem> BuildTranscriptTimeline(
        IReadOnlyList<TranscriptEvent> transcriptEvents,
        IReadOnlyList<TranslationEvent> translationEvents)
    {
        var translationByUtterance = translationEvents
            .Where(translation => !string.IsNullOrWhiteSpace(translation.UtteranceId))
            .GroupBy(translation => translation.UtteranceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(translation => translation.TimestampUtc)
                    .ThenBy(translation => translation.Sequence)
                    .Last(),
                StringComparer.OrdinalIgnoreCase);

        return transcriptEvents
            .OrderBy(transcriptEvent => transcriptEvent.Sequence)
            .ThenBy(transcriptEvent => transcriptEvent.TimestampUtc)
            .Select(transcriptEvent =>
            {
                var utteranceId = transcriptEvent.UtteranceId ?? string.Empty;
                var isEnglish = IsEnglish(transcriptEvent.DetectedLanguage);
                var isNonEnglish = !isEnglish && !string.IsNullOrWhiteSpace(transcriptEvent.DetectedLanguage);
                TranslationEvent? translationEvent = null;
                var hasTranslation = !string.IsNullOrWhiteSpace(utteranceId)
                    && translationByUtterance.TryGetValue(utteranceId, out translationEvent);

                return new TranscriptTimelineItem
                {
                    EventId = transcriptEvent.EventId,
                    UtteranceId = utteranceId,
                    Sequence = transcriptEvent.Sequence,
                    TimestampUtc = transcriptEvent.TimestampUtc,
                    TimestampDisplay = transcriptEvent.TimestampUtc.ToLocalTime().ToString("h:mm:ss tt"),
                    SpeakerDisplayLabel = !string.IsNullOrWhiteSpace(transcriptEvent.SpeakerDisplayLabel)
                        ? transcriptEvent.SpeakerDisplayLabel
                        : transcriptEvent.SpeakerId,
                    SpeakerRoleLabel = ToDisplayLabel(transcriptEvent.SpeakerRole),
                    SpeakerSourceLabel = GetSpeakerSourceLabel(transcriptEvent.SpeakerLabelSource),
                    Text = transcriptEvent.Text,
                    DetectedLanguageCode = NormalizeLanguageCode(transcriptEvent.DetectedLanguage),
                    DetectedLanguageLabel = GetLanguageLabel(transcriptEvent.DetectedLanguage),
                    LanguageAttribute = isNonEnglish ? NormalizeLanguageCode(transcriptEvent.DetectedLanguage) : string.Empty,
                    SourceIndicator = GetSourceIndicator(transcriptEvent.Source),
                    IsNonEnglish = isNonEnglish,
                    HasTranslation = hasTranslation,
                    TranslationButtonLabel = hasTranslation
                        ? $"Reveal English translation for utterance {transcriptEvent.Sequence}"
                        : $"Translation not available for utterance {transcriptEvent.Sequence}",
                    TranslationTargetLanguageLabel = hasTranslation ? GetLanguageLabel(translationEvent!.TargetLanguage) : "English",
                    TranslationText = hasTranslation ? translationEvent!.TranslatedText : string.Empty
                };
            })
            .ToArray();
    }

    private static SentimentPresentation BuildSentimentPresentation(SentimentFeedResponse sentimentFeed)
    {
        var summaryLabel = ToDisplayLabel(sentimentFeed.Summary.OverallLabel);
        var trendLabel = ToDisplayLabel(sentimentFeed.Summary.Trend);
        var updatedAt = sentimentFeed.Summary.UpdatedAtUtc != default
            ? sentimentFeed.Summary.UpdatedAtUtc
            : sentimentFeed.Events.OrderByDescending(item => item.TimestampUtc).FirstOrDefault()?.TimestampUtc ?? default;
        var latestEvent = sentimentFeed.Events
            .OrderByDescending(item => item.TimestampUtc)
            .ThenByDescending(item => item.RelatedTranscriptSequence ?? long.MinValue)
            .FirstOrDefault();

        if (latestEvent is null)
        {
            if (string.IsNullOrWhiteSpace(sentimentFeed.CallId) && string.IsNullOrWhiteSpace(sentimentFeed.Summary.CallId))
            {
                return SentimentPresentation.Waiting;
            }

            return new SentimentPresentation
            {
                HasData = true,
                ToneLabel = summaryLabel,
                TrendLabel = trendLabel,
                SummaryText = sentimentFeed.Summary.SummaryText,
                UpdatedDisplay = updatedAt == default ? "Awaiting update" : updatedAt.ToLocalTime().ToString("h:mm tt")
            };
        }

        var clampedScore = Math.Clamp(latestEvent.Score, -1d, 1d);
        var scorePercent = (int)Math.Round(((clampedScore + 1d) / 2d) * 100d, MidpointRounding.AwayFromZero);

        return new SentimentPresentation
        {
            HasData = true,
            ScorePercent = scorePercent,
            ToneLabel = summaryLabel,
            TrendLabel = trendLabel,
            SummaryText = sentimentFeed.Summary.SummaryText,
            UpdatedDisplay = updatedAt == default ? latestEvent.TimestampUtc.ToLocalTime().ToString("h:mm tt") : updatedAt.ToLocalTime().ToString("h:mm tt"),
            ScoreStateLabel = GetSentimentScoreState(scorePercent),
            ScoreVisualClass = GetSentimentScoreVisualClass(scorePercent)
        };
    }

    private static string GetSentimentScoreState(int scorePercent)
    {
        return scorePercent switch
        {
            <= 30 => "Escalation risk",
            < 55 => "Needs recovery",
            < 75 => "Stabilizing",
            _ => "Positive momentum"
        };
    }

    private static string GetSentimentScoreVisualClass(int scorePercent)
    {
        return scorePercent switch
        {
            <= 30 => "sentiment-meter-bar sentiment-meter-bar--negative",
            < 55 => "sentiment-meter-bar sentiment-meter-bar--caution",
            < 75 => "sentiment-meter-bar sentiment-meter-bar--steady",
            _ => "sentiment-meter-bar sentiment-meter-bar--positive"
        };
    }

    private string BuildConnectionSummary()
    {
        if (HasConnectionIssues)
        {
            return SessionWarning is null
                ? "Backend API degraded"
                : "Backend API disconnected";
        }

        if (CurrentSession.Call.CallId is null or "")
        {
            return "Backend connected • Waiting for active call";
        }

        var mode = CurrentSession.IsMockFeedActive ? "Mock feed active" : "Live feed active";
        var state = ToDisplayLabel(CurrentSession.Call.State);
        return $"{mode} • Call state: {state}";
    }

    private T ResolveResult<T>(
        ApiFetchResult<T> result,
        T fallback,
        string scope,
        Action<string> setWarning)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            return result.Value;
        }

        var warning = BuildUserFacingWarning(scope, result.FailureKind);
        setWarning(warning);
        _connectionIssues.Add(warning);
        _logger.LogWarning(
            "Backend API {Scope} unavailable ({FailureKind}). Detail: {Detail}. Rendering disconnected UI state.",
            scope,
            result.FailureKind?.ToString() ?? "unknown",
            result.ErrorMessage ?? "none");
        return fallback;
    }

    private static string BuildUserFacingWarning(string scope, ApiFetchFailureKind? failureKind)
    {
        return failureKind switch
        {
            ApiFetchFailureKind.Configuration => $"Backend API is not configured for {scope}.",
            ApiFetchFailureKind.Connectivity => $"Backend API is unreachable for {scope}.",
            ApiFetchFailureKind.Upstream => $"Backend API returned an error for {scope}.",
            ApiFetchFailureKind.Payload => $"Backend API returned invalid data for {scope}.",
            _ => $"Backend API is unavailable for {scope}."
        };
    }

    private static bool IsEnglish(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return true;
        }

        return languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMockLike(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.Contains("mock", StringComparison.OrdinalIgnoreCase)
            || source.Contains("script", StringComparison.OrdinalIgnoreCase)
            || source.Contains("deferred", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSpeakerSourceLabel(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "Diarization source unknown";
        }

        return IsMockLike(source) ? "Mock diarization" : "Live diarization";
    }

    private static string GetSourceIndicator(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "Source unknown";
        }

        return IsMockLike(source) ? "Mock source" : "Live source";
    }

    private static string GetLanguageLabel(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "Unknown";
        }

        var normalized = NormalizeLanguageCode(languageCode);
        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            return culture.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return normalized.ToUpperInvariant();
        }
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode) ? "und" : languageCode.Trim().ToLowerInvariant();
    }

    public static string ToDisplayLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return string.Join(' ',
            value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token =>
                {
                    if (token.Length == 0)
                    {
                        return token;
                    }

                    return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
                }));
    }

    public sealed record TranscriptTimelineItem
    {
        public string EventId { get; init; } = string.Empty;
        public string UtteranceId { get; init; } = string.Empty;
        public long Sequence { get; init; }
        public DateTimeOffset TimestampUtc { get; init; }
        public string TimestampDisplay { get; init; } = string.Empty;
        public string SpeakerDisplayLabel { get; init; } = string.Empty;
        public string SpeakerRoleLabel { get; init; } = string.Empty;
        public string SpeakerSourceLabel { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string DetectedLanguageCode { get; init; } = string.Empty;
        public string DetectedLanguageLabel { get; init; } = "Unknown";
        public string LanguageAttribute { get; init; } = string.Empty;
        public string SourceIndicator { get; init; } = string.Empty;
        public bool IsNonEnglish { get; init; }
        public bool HasTranslation { get; init; }
        public string TranslationButtonLabel { get; init; } = string.Empty;
        public string TranslationTargetLanguageLabel { get; init; } = "English";
        public string TranslationText { get; init; } = string.Empty;
    }

    public sealed record SentimentPresentation
    {
        public static SentimentPresentation Waiting { get; } = new();

        public bool HasData { get; init; }
        public int? ScorePercent { get; init; }
        public string ToneLabel { get; init; } = "Waiting for call";
        public string TrendLabel { get; init; } = "Awaiting sentiment";
        public string SummaryText { get; init; } = string.Empty;
        public string UpdatedDisplay { get; init; } = "Waiting for call";
        public string ScoreStateLabel { get; init; } = "Awaiting sentiment";
        public string ScoreVisualClass { get; init; } = "sentiment-meter-bar sentiment-meter-bar--steady";
    }
}
