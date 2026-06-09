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

    private volatile string? _callId;
    private int _repAddState = RepAddNone;

    /// <summary>Returns the current active call ID, or null if no call is in progress.</summary>
    public string? CallId => _callId;

    /// <summary>True once the rep participant has been added to the current call.</summary>
    public bool RepAdded => Volatile.Read(ref _repAddState) == RepAddDone;

    /// <summary>Sets the active call ID when a call is answered and resets rep-add state.</summary>
    public void SetCallId(string callId)
    {
        _callId = callId;
        Interlocked.Exchange(ref _repAddState, RepAddNone);
    }

    /// <summary>Clears the call ID when the call ends or the stream is completed.</summary>
    public void Clear()
    {
        _callId = null;
        Interlocked.Exchange(ref _repAddState, RepAddNone);
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
