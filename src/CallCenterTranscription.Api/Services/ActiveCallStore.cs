namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Singleton that holds the current active ACS call ID for the single concurrent call.
///
/// POC constraint (maxReplicas=1, single concurrent call): no distributed state needed.
/// The ACS IncomingCall handler calls SetCallId when AnswerCallAsync succeeds.
/// SpeechTranscriptionService reads CallId to route transcript events to the correct
/// SignalR group ("call:{callId}").
///
/// Rep-add idempotency: a tiny state machine (None → Adding → Added) guarantees the rep's
/// ACS identity is AddParticipant-ed to the call AT MOST ONCE even though two paths race to
/// add it — the CallConnected callback and the rep's /register call (whichever happens first
/// wins; the pending one reconverges). <see cref="TryBeginAddRep"/> atomically claims the
/// add; <see cref="MarkRepAdded"/> commits it; <see cref="ResetAddRep"/> releases the claim
/// so a failed attempt can be retried on the next /register.
/// </summary>
public sealed class ActiveCallStore
{
    private const int RepAddNone = 0;
    private const int RepAddInProgress = 1;
    private const int RepAddDone = 2;

    private readonly object _gate = new();
    private string? _callId;
    private DateTimeOffset? _startedAtUtc;
    private int _repAddState = RepAddNone;

    /// <summary>Returns the current active call ID, or null if no call is in progress.</summary>
    public string? CallId
    {
        get
        {
            lock (_gate)
            {
                return _callId;
            }
        }
    }

    /// <summary>Returns the stable timestamp captured when the current call became active.</summary>
    public DateTimeOffset? StartedAtUtc
    {
        get
        {
            lock (_gate)
            {
                return _startedAtUtc;
            }
        }
    }

    /// <summary>True once the rep participant has been added to the current call.</summary>
    public bool RepAdded => Volatile.Read(ref _repAddState) == RepAddDone;

    /// <summary>Sets the active call ID when a call is answered and resets rep-add state.</summary>
    public void SetCallId(string callId, DateTimeOffset? startedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            throw new ArgumentException("callId must be provided.", nameof(callId));
        }

        lock (_gate)
        {
            _callId = callId.Trim();
            _startedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
        }

        Interlocked.Exchange(ref _repAddState, RepAddNone);
    }

    /// <summary>Clears the call ID when the call ends or the stream is completed.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _callId = null;
            _startedAtUtc = null;
        }

        Interlocked.Exchange(ref _repAddState, RepAddNone);
    }

    /// <summary>Returns the current call ID and stable start time as an atomic snapshot.</summary>
    public ActiveCallSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new ActiveCallSnapshot(_callId, _startedAtUtc);
        }
    }

    /// <summary>
    /// Atomically claims the right to add the rep to the current call. Returns true to EXACTLY
    /// ONE caller (None → Adding); all other callers get false until the claim is released
    /// (<see cref="ResetAddRep"/>) or committed (<see cref="MarkRepAdded"/>).
    /// </summary>
    public bool TryBeginAddRep() =>
        Interlocked.CompareExchange(ref _repAddState, RepAddInProgress, RepAddNone) == RepAddNone;

    /// <summary>Commits the rep-add after AddParticipant succeeds (Adding → Added).</summary>
    public void MarkRepAdded() => Interlocked.Exchange(ref _repAddState, RepAddDone);

    /// <summary>Releases a failed rep-add claim so the next /register can retry (Adding → None).</summary>
    public void ResetAddRep() =>
        Interlocked.CompareExchange(ref _repAddState, RepAddNone, RepAddInProgress);
}

public readonly record struct ActiveCallSnapshot(string? CallId, DateTimeOffset? StartedAtUtc);
