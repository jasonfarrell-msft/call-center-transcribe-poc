# Skill: ACS Call Automation — Full Lifecycle Teardown

**Domain:** Azure Communication Services / Call Automation  
**Last updated:** 2026-06-10T10:46:25-04:00  
**Author:** Dyakka

---

## Problem This Solves

ACS Call Automation–answered calls have multiple termination paths (rep hangup, rep decline, customer hangup, abrupt drop). Each path fires different ACS events and may or may not close the media-stream WebSocket promptly. Without explicit handling of all paths, the backend call state (call ID, media claim, sentiment store) can linger indefinitely and the dashboard shows a phantom live call.

---

## ACS Event → Teardown Mapping

| Termination Path | Primary ACS Event | WebSocket Close? | Required Action |
|---|---|---|---|
| Rep calls `HangUpAsync(forEveryone:true)` | `CallDisconnected` | ✅ Yes (promptly) | None extra; finally-block handles it |
| Rep declines (`AddParticipantFailed`) | `CallDisconnected` (after our HangUp) | ✅ Yes | `HangUpAsync(forEveryone:true)` in `AddParticipantFailed` handler |
| Customer hangs up, rep NOT in call | `CallDisconnected` | ✅ Yes (quickly) | `CallDisconnected` handler as belt-and-suspenders |
| **Customer hangs up, rep IS in call** | `ParticipantsUpdated` (PSTN party left) | ❌ No (call stays alive) | `HangUpAsync(forEveryone:true)` in `ParticipantsUpdated` handler |
| Abrupt drop (any party) | `CallDisconnected` (eventually) | ⚠️ Maybe delayed | `CallDisconnected` handler runs direct teardown |

---

## The Critical Case: Customer Hangs Up While Rep is In Call

When the PSTN customer hangs up and the rep (VoIP CommunicationUser) is already in the call:
- ACS fires `ParticipantsUpdated` with a participant list that no longer includes a `PhoneNumberIdentifier`
- The call stays alive (rep's VoIP leg + Call Automation server bot leg remain)
- The media-stream WebSocket stays OPEN
- `CallDisconnected` does NOT fire until the remaining legs are terminated

**Fix:** In `ParticipantsUpdated` handler, if `Participants.Count > 0` and no `PhoneNumberIdentifier` present, call `HangUpAsync(forEveryone: true)` → ACS terminates the call → WebSocket close → finally-block teardown.

---

## Idempotent Teardown Pattern

Multiple paths can race to trigger teardown. Use an atomic claim on `ActiveCallStore`:

```csharp
// In ActiveCallStore:
private int _teardownState = 0;
private const int TeardownNone = 0, TeardownInProgress = 1;

public bool TryBeginTeardown() =>
    Interlocked.CompareExchange(ref _teardownState, TeardownInProgress, TeardownNone) == TeardownNone;
```

- Reset in `Clear()` (end of call) and `CompleteIncomingClaim()` (start of new call)
- One caller wins `TryBeginTeardown()` and owns: `CompleteStream` + `Clear` + `liveSentiment.Clear` + `CallEnded` broadcast
- Losing path: still calls `CompleteStream` (idempotent — `TryComplete()` returns false on second call, no throw) and always releases the media claim

---

## ForceCompleteCurrentSession Pattern

ACS callback handlers do not have access to the `Session` object created inside the WebSocket handler. Expose a current-session reference on `AcsAudioSource`:

```csharp
// In AcsAudioSource:
private volatile Session? _currentSession;

public Session BeginSession()
{
    var session = new Session(CreateFrameChannel());
    _currentSession = session;       // set before enqueue
    _sessions.Writer.TryWrite(session.Channel.Reader);
    return session;
}

public void CompleteStream(Session session)
{
    _currentSession = null;          // clear first
    session.Channel.Writer.TryComplete();
    // ... log
}

public void ForceCompleteCurrentSession()
{
    var s = _currentSession;         // volatile read
    if (s is not null) CompleteStream(s);
}
```

Call `ForceCompleteCurrentSession()` from `CallDisconnected` handler when it wins the teardown claim.

---

## Full Teardown Checklist

Both the WebSocket finally-block and the `CallDisconnected` callback must execute the same teardown steps when they win `TryBeginTeardown()`:

1. `acsSource.CompleteStream(session)` OR `acsSource.ForceCompleteCurrentSession()` — signals end-of-audio to `SpeechTranscriptionService`; recognizer loop exits naturally
2. `callStore.Clear()` — nulls `CallId`, resets all state machines (rep-add, incoming claim, rep-accepted, teardown state)
3. `liveSentiment.Clear()` — resets rolling sentiment panel to "Waiting"
4. Broadcast `CallEnded` via SignalR hub with captured `callId` (capture BEFORE `Clear()`)
5. `callStore.EndMediaClaim()` — ALWAYS called from WebSocket finally-block, never from callback (the media claim tracks the WebSocket lifetime, not the call lifetime)

---

## Guard: Empty ParticipantsUpdated

`ParticipantsUpdated` can fire with an empty list during early call setup (before PSTN party is added). Do NOT trigger hangup on empty list:

```csharp
// Only act if we have a participant list AND no PSTN party is in it.
if (updated.Participants.Count > 0 &&
    !updated.Participants.Any(p => p.Identifier is PhoneNumberIdentifier) &&
    !string.IsNullOrEmpty(callStore.CallId))
{
    // PSTN customer definitively left
}
```

---

## Audio Topology Note

This skill is lifecycle-only. The audio streaming topology (`MediaStreamingAudioChannel.Mixed`, `Pcm16KMono`, `StartMediaStreaming=true`) is never changed by teardown logic. R1 constraint: always use Mixed + Pcm16KMono.

---

## References

- `src/CallCenterTranscription.Api/AcsEndpoints.cs` — callback handler and WebSocket finally-block
- `src/CallCenterTranscription.Api/Services/ActiveCallStore.cs` — teardown claim state machine
- `src/CallCenterTranscription.Telephony/AcsAudioSource.cs` — session lifecycle
