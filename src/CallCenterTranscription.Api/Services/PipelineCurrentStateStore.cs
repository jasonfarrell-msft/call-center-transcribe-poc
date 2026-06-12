using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Api.Services;

public sealed class PipelineCurrentStateStore
{
    private readonly object _gate = new();
    private readonly bool _liveAcsMode;
    private readonly SessionCurrentResponse? _mockSession;
    private PipelineCurrentStateResponse _currentState;

    public PipelineCurrentStateStore(
        IScriptedScenarioFeed scriptedScenarioFeed,
        IConfiguration configuration)
    {
        _liveAcsMode = string.Equals(
            configuration.GetValue<string>("AudioSource:Mode"),
            "Acs",
            StringComparison.OrdinalIgnoreCase);

        if (_liveAcsMode)
        {
            _currentState = CreateIdleState(
                isMockFeedActive: false,
                source: "acs-live",
                streamReplayPolicy: "live_lifecycle_only");
        }
        else
        {
            _mockSession = scriptedScenarioFeed.GetCurrentSession();
            _currentState = scriptedScenarioFeed.GetCurrentState();
        }
    }

    public PipelineCurrentStateResponse GetSnapshot()
    {
        lock (_gate)
        {
            return _currentState;
        }
    }

    public SessionCurrentResponse GetCurrentSession()
    {
        if (!_liveAcsMode && _mockSession is not null)
        {
            return _mockSession;
        }

        var snapshot = GetSnapshot();
        return new SessionCurrentResponse
        {
            Call = snapshot.Call,
            SentimentSummary = snapshot.SentimentSummary,
            IsMockFeedActive = snapshot.IsMockFeedActive,
            Notes = _liveAcsMode
                ? BuildLiveModeNotes(snapshot.Call.State)
                : $"Deterministic scripted feed using {snapshot.Call.Source}."
        };
    }

    public ActiveCallStateResponse GetActiveCall()
    {
        var snapshot = GetSnapshot();
        var state = NormalizeCallState(snapshot.Call.State);
        return new ActiveCallStateResponse
        {
            CallId = snapshot.Call.CallId,
            State = state,
            AcceptAvailable = string.Equals(state, "pending", StringComparison.Ordinal),
            RepAccepted = IsAcceptedState(state),
            StartedAtUtc = string.IsNullOrWhiteSpace(snapshot.Call.CallId)
                ? null
                : snapshot.Call.StartedAtUtc
        };
    }

    public void MarkPending(string callId, DateTimeOffset? startedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        lock (_gate)
        {
            var effectiveStart = startedAtUtc ?? _currentState.Call.StartedAtUtc;
            if (effectiveStart == default)
            {
                effectiveStart = DateTimeOffset.UtcNow;
            }

            _currentState = _currentState with
            {
                Call = _currentState.Call with
                {
                    CallId = callId,
                    SessionId = string.IsNullOrWhiteSpace(_currentState.Call.SessionId)
                        ? $"session-{callId}"
                        : _currentState.Call.SessionId,
                    CustomerName = string.IsNullOrWhiteSpace(_currentState.Call.CustomerName)
                        ? "Inbound caller"
                        : _currentState.Call.CustomerName,
                    AgentName = string.IsNullOrWhiteSpace(_currentState.Call.AgentName)
                        ? "Representative"
                        : _currentState.Call.AgentName,
                    QueueName = string.IsNullOrWhiteSpace(_currentState.Call.QueueName)
                        ? "Live Queue"
                        : _currentState.Call.QueueName,
                    State = "pending",
                    StartedAtUtc = effectiveStart,
                    ScenarioName = string.IsNullOrWhiteSpace(_currentState.Call.ScenarioName)
                        ? "Live ACS call"
                        : _currentState.Call.ScenarioName,
                    Source = _liveAcsMode ? "acs-live" : _currentState.Call.Source
                },
                IsMockFeedActive = !_liveAcsMode && _currentState.IsMockFeedActive,
                GeneratedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public void MarkAccepted(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        lock (_gate)
        {
            if (!string.Equals(_currentState.Call.CallId, callId, StringComparison.Ordinal))
            {
                return;
            }

            _currentState = _currentState with
            {
                Call = _currentState.Call with
                {
                    State = "accepted"
                },
                GeneratedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public void ClearActiveCall(string? callId = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(callId) &&
                !string.Equals(_currentState.Call.CallId, callId, StringComparison.Ordinal))
            {
                return;
            }

            _currentState = CreateIdleState(
                isMockFeedActive: !_liveAcsMode && _currentState.IsMockFeedActive,
                source: _liveAcsMode ? "acs-live" : "mock-script",
                streamReplayPolicy: _liveAcsMode ? "live_lifecycle_only" : "full_history_for_active_call");
        }
    }

    private static string NormalizeCallState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "idle" : state.Trim().ToLowerInvariant();

    private static bool IsAcceptedState(string state) =>
        string.Equals(state, "accepted", StringComparison.Ordinal) ||
        string.Equals(state, "active", StringComparison.Ordinal) ||
        string.Equals(state, "live", StringComparison.Ordinal);

    private static string BuildLiveModeNotes(string state) => NormalizeCallState(state) switch
    {
        "pending" => "Incoming ACS call is pending rep acceptance; no transcript is replayed before the rep accepts.",
        "accepted" => "Rep has accepted the live ACS call; transcript will begin once customer speech is recognized.",
        _ => "Waiting for the next live ACS call."
    };

    private static PipelineCurrentStateResponse CreateIdleState(
        bool isMockFeedActive,
        string source,
        string streamReplayPolicy) => new()
    {
        Call = new CallSessionMetadata
        {
            State = "idle",
            Source = source
        },
        SentimentSummary = new CallSentimentSummary
        {
            Source = source,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        },
        IsMockFeedActive = isMockFeedActive,
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        StreamReplayPolicy = streamReplayPolicy
    };
}
