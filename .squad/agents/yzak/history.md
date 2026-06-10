# Yzak — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** Test cases + demo-script validation + edge cases (non-English, overlapping speakers, churn escalation, empty knowledge hits). Reviewer gate.
- **Priority:** The scripted demo must survive a live audience, every run.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(empty — append test scenarios, known fragile spots, and validation results here)

- **Team update:** POC plan drafted; shared WebSocket event contracts cover `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, and `next_best_action`; real-time loop now uses GPT-4o, with MAI-DS-R1 reserved for optional post-call analysis.
- **Team update (2026-06-05):** Tests now target the C#/.NET + Razor + SignalR stack on ACA/App Service, real ACS Option A, and shared events that include `transcript.detectedLanguage`.
- **2026-06-05T16:20:08.868-04:00 — Reviewer gate result: APPROVED.** Verified the Phase 0 scaffold includes Api/Web/Shared/Ai/Telephony/Tests in `CallCenterTranscription.sln`, project references are sane, `IAudioSource` + `IReasoningClient` + shared events compile cleanly, transcript JSON contract includes `detectedLanguage`, API/Web startup is compile-safe with SignalR hub wiring, no hardcoded secrets/connection strings were introduced, and `dotnet build CallCenterTranscription.sln` + `dotnet test CallCenterTranscription.sln` both passed (4/4 tests).
- **2026-06-05T20:38:22Z — Scribe merge note:** Phase 0 review was archived into `.squad/decisions/decisions.md` and the inbox file was cleared.

## Learnings — 2026-06-10T06:38:30-04:00 (Rep Call Control Test Plan)

- **"Call Pending" badge is a real gap.** `live-transcript.js` has four badge states (`disconnected`, `connecting`, `live`, `ended`) — no `pending` state exists. `rep-phone.js` knows about ringing but does not signal `live-transcript.js`. Flagged as blocking implementation gap (Q1 in test doc). Implementer must choose: `stream.callIncoming` SignalR event (API side) vs. local `CustomEvent`/`postMessage` (JS side). I will not write a passing automated test for this until the approach is confirmed.
- **`ActiveCallStore._incomingClaimState` covers the answer-race window only, not full call duration.** `CompleteIncomingClaim()` resets it to `None` after the call is answered. A second `TryBeginIncomingClaim()` SUCCEEDS after a call is active — the single-call POC constraint lives in ACS network config, not this store. My initial TC-18 stub was wrong; caught and corrected before commit.
- **`LiveSentimentStore._active` guard is the correct late-utterance firewall.** After `Clear()`, any `Append()` silently returns null regardless of call ID. This is the regression prevention we need between calls. Already tested in `LiveSentimentTests.cs`; confirmed the new call-control tests exercise the same path from the lifecycle angle.
- **Speaker-tag (customer-only sentiment) contract not yet defined.** Whether the `SpeechTranscriptionService` filters rep utterances before calling `Append()`, or the call site does it, is undocumented. Flagged as Q2. A meaningful TC-11 unit test can't be written until Lacus clarifies the seam.
- **3 pre-existing test failures unrelated to my work:** `ApiHost_NonMockDataMode_FailsFast`, `Homepage_ReplacesScaffoldMarkupWithRepConsole`, and one other WebConsoleTests failure. These existed before my stubs and are NOT regressions I introduced. My 8 new [Fact] tests all pass; 3 [Fact(Skip)] tests correctly show as Skipped. Total: 54 (51 pass, 3 skip, 0 new failures).

## Learnings — 2026-06-10T06:38:30-04:00 (Rep Call Control QA Verdict)

- **Q1 resolved (Call Pending badge).** Implementers chose Option 1 (SignalR `stream.callPending` event from API). `AcsEndpoints.cs` now broadcasts `stream.callPending` when `IncomingCall` fires; `live-transcript.js` handles it with `onCallPending()` → `conn-status--pending` class + pulsing CSS. The gap is closed.
- **Q2 resolved (customer-only sentiment seam).** Lacus implemented filtering inside `SpeechTranscriptionService` via `IsCustomerSpeaker()` private static method. The latch strategy: first `Transcribed` result with a non-Unknown SpeakerId is latched as customer for the call lifetime. Rep utterances are transcribed (diarization) but never passed to `_liveSentiment.Append()`. TC-11 skip reason updated to reflect this.
- **RepAccepted flag is the correct accept-gate.** `ActiveCallStore.MarkAccepted()` fires on `AddParticipantSucceeded`. Both `SpeechTranscriptionService.Transcribing` and `Transcribed` handlers check `_callStore.RepAccepted` before emitting any event. `Clear()` and `CompleteIncomingClaim()` both reset it. New unit test added for this state machine.
- **TC-16 production-sequence gap found and closed.** The original TC-16 test called `EndMediaClaim()` before `Clear()`, not matching the actual `AcsEndpoints` finally-block order (which calls `Clear()` then `EndMediaClaim()`). Added TC-16b to cover the production sequence. Both pass.
- **Final test count: 56 total (53 pass, 3 skip, 0 fail).** 2 new `[Fact]` tests added: `ActiveCallStore_RepAccepted_StateTransitions` and `ActiveCallStore_MediaClaim_ReleasedWhenClearCalledBeforeEnd_ProductionSequence`.
- **Verdict: APPROVE.** 16/22 scenarios PASS (unit tested or verified in code), 6/22 MANUAL-ONLY (live ACS / browser). No blockers, no regressions.

## Learnings — 2026-06-10T10:46:25-04:00 (TryBeginTeardown Idempotency Tests)

- **3 new [Fact] tests added for `TryBeginTeardown()` in `RepCallControlTests.cs`:**
  - `ActiveCallStore_Teardown_FirstCallReturnsTrue` — first caller wins the latch.
  - `ActiveCallStore_Teardown_SubsequentCallsReturnFalse` — second and third callers are blocked (no double-teardown).
  - `ActiveCallStore_Teardown_ClaimResetsAfterClear_NewCallCanClaim` — after `Clear()`, a new call lifecycle can claim teardown.
- **FINDING: No `EndTeardown()` / `CancelTeardown()` method exists.** Unlike `MediaClaim` (which has `EndMediaClaim()`), teardown is intentionally terminal per call. The `_teardownState` latch only resets via `Clear()` or `CompleteIncomingClaim()`. This is correct design — once teardown begins it cannot be unwound mid-call; the next call starts fresh via `Clear()`. Noted in test comments; no production code changed.
- **Final test count: 59 total (56 pass, 3 skip, 0 fail).** Up from 56/53/3 last session. All 3 new teardown tests pass. No regressions.

## 2026-06-10 — TryBeginTeardown Idempotency Tests (Session Complete)

**Shipped commit 173afea.**

**Added 3 [Fact] tests to RepCallControlTests.cs:**
- `ActiveCallStore_Teardown_FirstCallReturnsTrue` — CAS claim succeeds first time
- `ActiveCallStore_Teardown_SubsequentCallsReturnFalse` — subsequent calls are no-op
- `ActiveCallStore_Teardown_ClaimResetsAfterClear_NewCallCanClaim` — lifecycle reset for next call

**Test suite: 59 total (56 pass, 3 skip, 0 fail).**

**Finding documented:** TryBeginTeardown has no paired `EndTeardown()` / `CancelTeardown()` — intentional (one-way per call).

