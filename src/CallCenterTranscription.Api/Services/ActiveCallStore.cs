namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Singleton that holds the current active ACS call ID for the single concurrent call.
///
/// POC constraint (maxReplicas=1, single concurrent call): no distributed state needed.
/// The ACS IncomingCall handler calls SetCallId when AnswerCallAsync succeeds.
/// SpeechTranscriptionService reads CallId to route transcript events to the correct
/// SignalR group ("call:{callId}").
/// </summary>
public sealed class ActiveCallStore
{
    private volatile string? _callId;

    /// <summary>Returns the current active call ID, or null if no call is in progress.</summary>
    public string? CallId => _callId;

    /// <summary>Sets the active call ID when a call is answered.</summary>
    public void SetCallId(string callId) => _callId = callId;

    /// <summary>Clears the call ID when the call ends or the stream is completed.</summary>
    public void Clear() => _callId = null;
}
