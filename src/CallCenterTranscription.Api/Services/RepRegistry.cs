namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Holds the ACS identity (userId) of the rep whose browser is currently ready to receive an
/// inbound VoIP leg. The BROWSER is the source of truth: it persists its own userId, fetches a
/// token for it, wires its incomingCall handler, and only THEN calls /api/rep/register — so a
/// "registered" rep is always one whose handler is live. A ~15s heartbeat re-register keeps this
/// fresh and auto-reconverges after an API restart (which clears this in-memory state).
///
/// POC scope: single rep, single concurrent call → last-writer-wins, no per-rep fan-out.
/// </summary>
public sealed class RepRegistry
{
    private volatile string? _userId;
    private long _lastSeenUtcTicks;

    /// <summary>The currently registered rep ACS userId, or null if none is registered.</summary>
    public string? CurrentUserId => _userId;

    /// <summary>UTC time of the most recent register/heartbeat for the current rep.</summary>
    public DateTimeOffset LastSeenUtc => new(Volatile.Read(ref _lastSeenUtcTicks), TimeSpan.Zero);

    /// <summary>Registers (or heartbeats) the rep identity that should receive the next call leg.</summary>
    public void Register(string userId)
    {
        _userId = userId;
        Volatile.Write(ref _lastSeenUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>Clears the registered rep (e.g., on explicit logout).</summary>
    public void Clear() => _userId = null;
}
