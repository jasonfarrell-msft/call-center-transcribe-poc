# Dyakka — History

## Current Focus (2026-06-10)

### Customer (PSTN) Hangup Teardown — COMPLETE ✅

**Shipped commit 173afea.**

**Gap fixed:** When PSTN customer hangs up while rep is in call, ACS fires `ParticipantsUpdated` but call stayed alive (no teardown, phantom live call on dashboard).

**Solution:**
- `ParticipantsUpdated` handler: detects PSTN party left, calls `HangUpAsync(forEveryone:true)`
- `CallDisconnected` belt-and-suspenders fallback
- `TryBeginTeardown()` CAS latch ensures exactly one teardown path wins (WebSocket finally-block OR callback)
- Result: Call state cleaned, `callEnded` broadcast fires

**Idempotency:** 3-layer approach (TryBeginTeardown CAS, volatile _currentSession, idempotent Channel.TryComplete)

**Files:** `ActiveCallStore.cs` (TryBeginTeardown + _teardownState), `AcsAudioSource.cs` (ForceCompleteCurrentSession), `AcsEndpoints.cs` (ParticipantsUpdated/CallDisconnected handlers)

**Build: 0 errors, 0 warnings. Tests: 56 pass, 3 skip, 0 fail.**

**Review: Athrun APPROVED (2 non-blocking Phase 2 advisories).**

---

## Prior Sessions — Summary

### Initial Delivery (2026-06-08): ACS Option C Plumbing

Implemented WebSocket media-streaming plumbing (AcsAudioSource, routes, DI swap) with mock audio default. Build clean, tests pass. Decisions documented in decisions.md.

**Key context:**
- ACS media streaming: PCM 16-bit mono, 50fps, JSON frames (base64-encoded)
- Managed identity + zero secrets in code
- DI: `AudioSource:Mode` config (default "Mock", flip to "Acs" for live)
- Deferred: PSTN number purchase, Event Grid subscription, Entra auth

### US Number Feasibility (2026-06-08)

ACS dataLocation controls number geography (immutable at create time). Can switch from Europe→United States; requires delete+reprovision. Path documented for operator.

### Rep Call-Control Lifecycle (2026-06-10)

Shipped full Accept/Reject/Hangup flow (Tasks 1–5):
- New events: CallPending, CallAccepted (replaces old CallStarted)
- ActiveCallStore: RepAccepted flag for sentiment gating
- AcsEndpoints: AddParticipantSucceeded broadcasts CallAccepted; AddParticipantFailed calls HangUpAsync
- RepEndpoints: new POST /api/rep/hangup for rep-initiated teardown
- Web: proxy + rep-phone.js hangup handler

Build clean, ready for customer-hangup teardown follow-up.

---

## Archive: Technical Research & Decisions

- ACS audio streaming: Media Streaming GA, mixed/unmixed modes, WebSocket JSON frames
- Call topology: Inbound PSTN → Answer → AddParticipant (rep) → two-party audio streaming
- Authentication: Microsoft Entra / Managed Identity (no connection strings)
- Event Grid: IncomingCall via subscription, callbacks via URI at answer time
- Known gotchas: 30-second ring max, public HTTPS endpoint required, minReplicas=1 for demo reliability

---

## Contact / Handoff

All code committed to origin/main (commit 173afea). All decisions in decisions.md. Team is coordinating on:
1. Event Grid + Entra delivery auth wiring (Meyrin/Lacus)
2. Operator: ACS resource delete + reprovision (dataLocation flip)
3. Operator: Verify subscription eligibility, purchase US toll-free

Dyakka ready for next round: Event Grid wiring, live PSTN integration, audio→Speech consumer coordination.
