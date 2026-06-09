using CallCenterTranscription.Shared.Events;
using Microsoft.Extensions.Configuration;

namespace CallCenterTranscription.Api.Services;

public sealed class PipelineCurrentStateStore
{
    private readonly object _gate = new();
    private readonly bool _mockMode;
    private readonly IScriptedScenarioFeed _scriptedScenarioFeed;
    private readonly ActiveCallStore _activeCallStore;
    private readonly LiveSentimentStore _liveSentimentStore;
    private string? _trackedCallId;
    private readonly List<TranscriptEvent> _transcriptEvents = [];
    private readonly List<TranslationEvent> _translationEvents = [];
    private readonly List<ChurnRiskEvent> _churnRiskEvents = [];
    private readonly List<KnowledgeCardEvent> _knowledgeCardEvents = [];
    private readonly List<NextBestActionEvent> _nextBestActionEvents = [];

    public PipelineCurrentStateStore(
        IScriptedScenarioFeed scriptedScenarioFeed,
        ActiveCallStore activeCallStore,
        LiveSentimentStore liveSentimentStore,
        IConfiguration configuration)
    {
        _mockMode = string.Equals(
            configuration.GetValue<string>("AudioSource:Mode") ?? "Acs",
            "Mock",
            StringComparison.OrdinalIgnoreCase);
        _scriptedScenarioFeed = scriptedScenarioFeed;
        _activeCallStore = activeCallStore;
        _liveSentimentStore = liveSentimentStore;
    }

    public PipelineCurrentStateResponse GetSnapshot()
    {
        if (_mockMode)
        {
            return _scriptedScenarioFeed.GetCurrentState();
        }

        var activeCall = _activeCallStore.GetSnapshot();
        var callId = activeCall.CallId ?? string.Empty;
        var hasActiveCall = !string.IsNullOrWhiteSpace(callId);
        var liveSentiment = _liveSentimentStore.GetFeed();
        IReadOnlyList<TranscriptEvent> transcriptEvents;
        IReadOnlyList<TranslationEvent> translationEvents;
        IReadOnlyList<ChurnRiskEvent> churnRiskEvents;
        IReadOnlyList<KnowledgeCardEvent> knowledgeCardEvents;
        IReadOnlyList<NextBestActionEvent> nextBestActionEvents;

        lock (_gate)
        {
            var isTrackedActiveCall = hasActiveCall && string.Equals(_trackedCallId, callId, StringComparison.Ordinal);
            transcriptEvents = isTrackedActiveCall ? _transcriptEvents.ToArray() : [];
            translationEvents = isTrackedActiveCall ? _translationEvents.ToArray() : [];
            churnRiskEvents = isTrackedActiveCall ? _churnRiskEvents.ToArray() : [];
            knowledgeCardEvents = isTrackedActiveCall ? _knowledgeCardEvents.ToArray() : [];
            nextBestActionEvents = isTrackedActiveCall ? _nextBestActionEvents.ToArray() : [];
        }

        var sentimentSummary = liveSentiment.Events.Count > 0
            ? liveSentiment.Summary with { CallId = callId }
            : new CallSentimentSummary
            {
                CallId = callId,
                OverallLabel = "neutral",
                Trend = "steady",
                SummaryText = hasActiveCall
                    ? string.Empty
                    : "Waiting for live customer-to-representative interaction.",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Source = "acs-live"
            };

        return new PipelineCurrentStateResponse
        {
            Call = new CallSessionMetadata
            {
                CallId = callId,
                State = hasActiveCall ? "active" : "waiting",
                StartedAtUtc = hasActiveCall ? activeCall.StartedAtUtc ?? default : default,
                Source = "acs-live"
            },
            SentimentSummary = sentimentSummary,
            IsMockFeedActive = false,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            StreamReplayPolicy = "full_history_for_active_call",
            TranscriptEvents = transcriptEvents,
            TranslationEvents = translationEvents,
            SentimentEvents = liveSentiment.Events,
            ChurnRiskEvents = churnRiskEvents,
            KnowledgeCardEvents = knowledgeCardEvents,
            NextBestActionEvents = nextBestActionEvents
        };
    }

    public void ResetForCall(string callId)
    {
        if (_mockMode || string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        lock (_gate)
        {
            _trackedCallId = callId.Trim();
            ClearEventsLocked();
        }
    }

    public void ClearLiveState()
    {
        if (_mockMode)
        {
            return;
        }

        lock (_gate)
        {
            _trackedCallId = null;
            ClearEventsLocked();
        }
    }

    public void AppendTranscriptEvent(TranscriptEvent transcriptEvent)
    {
        if (_mockMode || transcriptEvent is null || !transcriptEvent.IsFinal)
        {
            return;
        }

        lock (_gate)
        {
            if (!TryTrackActiveCallLocked(transcriptEvent.CallId))
            {
                return;
            }

            _transcriptEvents.Add(transcriptEvent);
        }
    }

    public void AppendTranslationEvent(TranslationEvent translationEvent)
    {
        if (_mockMode || translationEvent is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!TryTrackActiveCallLocked(translationEvent.CallId))
            {
                return;
            }

            _translationEvents.Add(translationEvent);
        }
    }

    public void AppendChurnRiskEvent(ChurnRiskEvent churnRiskEvent)
    {
        if (_mockMode || churnRiskEvent is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!TryTrackActiveCallLocked(churnRiskEvent.CallId))
            {
                return;
            }

            _churnRiskEvents.Add(churnRiskEvent);
        }
    }

    public void AppendKnowledgeCardEvent(KnowledgeCardEvent knowledgeCardEvent)
    {
        if (_mockMode || knowledgeCardEvent is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!TryTrackActiveCallLocked(knowledgeCardEvent.CallId))
            {
                return;
            }

            _knowledgeCardEvents.Add(knowledgeCardEvent);
        }
    }

    public void AppendNextBestActionEvent(NextBestActionEvent nextBestActionEvent)
    {
        if (_mockMode || nextBestActionEvent is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!TryTrackActiveCallLocked(nextBestActionEvent.CallId))
            {
                return;
            }

            _nextBestActionEvents.Add(nextBestActionEvent);
        }
    }

    private bool TryTrackActiveCallLocked(string? callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        var activeCall = _activeCallStore.GetSnapshot();
        if (!string.Equals(activeCall.CallId, callId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(_trackedCallId, callId, StringComparison.Ordinal))
        {
            _trackedCallId = callId;
            ClearEventsLocked();
        }

        return true;
    }

    private void ClearEventsLocked()
    {
        _transcriptEvents.Clear();
        _translationEvents.Clear();
        _churnRiskEvents.Clear();
        _knowledgeCardEvents.Clear();
        _nextBestActionEvents.Clear();
    }
}
