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
    private const int IncomingClaimNone = 0;
    private const int IncomingClaimInProgress = 1;
    private const int MediaClaimNone = 0;
    private const int MediaClaimInProgress = 1;
    private const int CallConnectedFalse = 0;
    private const int CallConnectedTrue = 1;
    private const int RepAcceptedFalse = 0;
    private const int RepAcceptedTrue  = 1;
    private const int TeardownNone = 0;
    private const int TeardownInProgress = 1;

    private volatile string? _callId;
    private int _repAddState = RepAddNone;
    private int _incomingClaimState = IncomingClaimNone;
    private int _mediaClaimState = MediaClaimNone;
    private int _callConnected = CallConnectedFalse;
    private int _repAccepted = RepAcceptedFalse;
    private int _teardownState = TeardownNone;
    private readonly object _teardownGate = new();

    /// <summary>Returns the current active call ID, or null if no call is in progress.</summary>
    public string? CallId => _callId;

    /// <summary>True once the rep has clicked Accept and ACS confirmed the join (AddParticipantSucceeded).</summary>
    /// <remarks>Lacus reads this flag to know whether to gate sentiment scoring. Do not write from outside this class.</remarks>
    public bool RepAccepted => Volatile.Read(ref _repAccepted) == RepAcceptedTrue;

    /// <summary>True once ACS has emitted CallConnected for the current call.</summary>
    public bool CallConnected => Volatile.Read(ref _callConnected) == CallConnectedTrue;

    /// <summary>Marks the active call as fully connected at the ACS platform layer.</summary>
    public void MarkConnected(string callId)
    {
        if (!string.Equals(_callId, callId, StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.Exchange(ref _callConnected, CallConnectedTrue);
    }

    /// <summary>Marks the rep as having accepted the call (called from AddParticipantSucceeded callback).</summary>
    public void MarkAccepted(string callId)
    {
        if (!string.Equals(_callId, callId, StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.Exchange(ref _repAccepted, RepAcceptedTrue);
    }

    /// <summary>True once the rep participant has been added to the current call.</summary>
    public bool RepAdded => Volatile.Read(ref _repAddState) == RepAddDone;

    /// <summary>Sets the active call ID when a call is answered and resets rep-add state.</summary>
    public void SetCallId(string callId)
    {
        Interlocked.Exchange(ref _repAddState, RepAddNone);
        Interlocked.Exchange(ref _callConnected, CallConnectedFalse);
        _callId = callId;
    }

    /// <summary>Clears the call ID when the call ends or the stream is completed.</summary>
    public void Clear(string? callId = null)
    {
        if (!string.IsNullOrWhiteSpace(callId) &&
            !string.Equals(_callId, callId, StringComparison.Ordinal))
        {
            return;
        }

        _callId = null;
        Interlocked.Exchange(ref _repAddState, RepAddNone);
        Interlocked.Exchange(ref _incomingClaimState, IncomingClaimNone);
        Interlocked.Exchange(ref _callConnected, CallConnectedFalse);
        Interlocked.Exchange(ref _repAccepted, RepAcceptedFalse);
        Interlocked.Exchange(ref _teardownState, TeardownNone);
    }

    /// <summary>Claims ownership of answering the next incoming call.</summary>
    public bool TryBeginIncomingClaim() =>
        Interlocked.CompareExchange(ref _incomingClaimState, IncomingClaimInProgress, IncomingClaimNone) == IncomingClaimNone;

    /// <summary>Commits the active call after AnswerCall succeeds and clears the incoming claim.</summary>
    public void CompleteIncomingClaim(string callId)
    {
        Interlocked.Exchange(ref _repAddState, RepAddNone);
        Interlocked.Exchange(ref _callConnected, CallConnectedFalse);
        Interlocked.Exchange(ref _repAccepted, RepAcceptedFalse);
        Interlocked.Exchange(ref _teardownState, TeardownNone);
        _callId = callId;
        Interlocked.Exchange(ref _incomingClaimState, IncomingClaimNone);
    }

    /// <summary>Releases a failed incoming-call answer claim so a later event may retry.</summary>
    public void CancelIncomingClaim() =>
        Interlocked.CompareExchange(ref _incomingClaimState, IncomingClaimNone, IncomingClaimInProgress);

    /// <summary>Claims ownership of the active ACS media-stream WebSocket session.</summary>
    public bool TryBeginMediaClaim() =>
        Interlocked.CompareExchange(ref _mediaClaimState, MediaClaimInProgress, MediaClaimNone) == MediaClaimNone;

    /// <summary>Releases the active ACS media-stream WebSocket session claim.</summary>
    public void EndMediaClaim() =>
        Interlocked.CompareExchange(ref _mediaClaimState, MediaClaimNone, MediaClaimInProgress);

    /// <summary>
    /// Atomically claims the right to run teardown for the current call (e.g., broadcast
    /// CallEnded, clear store). Returns true to EXACTLY ONE caller; all other concurrent or
    /// subsequent callers get false until <see cref="Clear"/> resets state for the next call.
    ///
    /// Used to prevent a race between the media-stream WebSocket finally-block and the
    /// <c>CallDisconnected</c> ACS callback both attempting a full teardown simultaneously.
    /// </summary>
    public bool TryBeginTeardown(string? callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        lock (_teardownGate)
        {
            if (!string.Equals(_callId, callId, StringComparison.Ordinal))
            {
                return false;
            }

            if (_teardownState != TeardownNone)
            {
                return false;
            }

            _teardownState = TeardownInProgress;
            return true;
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
    public void MarkRepAdded(string callId)
    {
        if (!string.Equals(_callId, callId, StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.Exchange(ref _repAddState, RepAddDone);
    }

    /// <summary>Releases a failed rep-add claim so the next /register can retry (Adding → None).</summary>
    public void ResetAddRep(string callId)
    {
        if (!string.Equals(_callId, callId, StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.CompareExchange(ref _repAddState, RepAddNone, RepAddInProgress);
    }
}
