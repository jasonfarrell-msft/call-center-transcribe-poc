# Yzak — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** Test cases + demo-script validation + edge cases (non-English, overlapping speakers, churn escalation, empty knowledge hits). Reviewer gate.
- **Priority:** The scripted demo must survive a live audience, every run.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(append test scenarios, known fragile spots, and validation results here)

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

## Learnings — 2026-06-10T11:20:14-04:00 (Phase-2B Speaker Edge-Case Documentation Test)

- **Documented known limitation pinned by test.** `Phase2B_CustomerSpeaksFirstPostAccept_NoPreAcceptSpeech` in `SpeakerAttributionStateTests.cs` captures the residual Phase-2B edge case identified in Athrun's review: if the customer speaks first post-accept with NO pre-accept speech, Phase 2B cannot distinguish who greeted and latches the customer as Rep and the rep as Customer (label flip). Test asserts the *current* (known-wrong) behavior — it is green today and will turn red when the limitation is fixed, providing automatic visibility.
- **Scenario is demo-unlikely** (requires customer to speak before rep greeting AND complete silence before accept), but the pinned test ensures it cannot regress silently.
- **Final test count: 78 total (75 pass, 3 skip, 0 fail).** Up from 59 pre-Lacus; Lacus's 14 SpeakerAttributionStateTests + this 1 documentation test = 15 new since the teardown session.

## Learnings — 2026-06-11T15:41:04.207-04:00 (Diarization Role Bug Fix Review)

- **Verdict: APPROVE.** Lacus's uncommitted fix correctly enforces the caller-order rule: first observed known speaker = Customer, second distinct speaker = Rep. This directly resolves the user-reported "everything is Rep" bug.
- **Root cause confirmed.** The prior Phase-2B fallback (`first post-accept speaker = Rep`) was inverted for this inbound topology where the customer initiates the call. When customer spoke first post-accept (the common case), they were latched as Rep, making the entire transcript show Rep labels.
- **Fix design.** Collapsed Phase 2A / Phase 2B into a single rule: first known speaker = Customer always (whether pre-accept or post-accept). This is sound because: (a) pre-accept, only the customer is on the Mixed audio stream; (b) post-accept, the user confirmed "the customer will call in first, rep joins second."
- **Test coverage.** 20 tests pass including: Phase 1 pre-accept paths, Phase 2B both-speakers-post-accept, Unknown filtering, immutability after latch, and return-value logging contracts. The old documentation test (`Phase2B_CustomerSpeaksFirstPostAccept_NoPreAcceptSpeech`) is correctly superseded — the new `Phase2B_FirstSpeakerPostAccept_IsLatchedAsCustomer` asserts the fixed (correct) behavior directly.
- **Integration verified.** `SpeechTranscriptionService.Transcribed` handler calls `attribution.Observe()` on every event, then `attribution.IsCustomer()` feeds into `BuildTranscriptEvent()` which sets `SpeakerRole = "customer" | "rep" | "unknown"`. Sentiment routing (`_liveSentiment.Append()`) is gated on `isCustomer == true`. No dead code paths.
- **Residual known limitation (unchanged).** If the rep speaks first post-accept AND the customer was completely silent pre-accept, the rep gets labeled Customer. User confirmed this is not their flow. Demo-survivable.



## Learnings — 2026-06-11T15:36:11.935-04:00 (Synthetic Answer Data Review)

- **Verdict: APPROVE with targeted corrections applied.** Kira's new JSONL corpus already covered billing, emergency/safety, will-call vs keep-full, competitor churn, cancellation, fee waivers, delay updates, and minimum-delivery objections well enough for a live demo.
- **Most obvious retrieval gap was Spanish competitor pricing.** The scripted retention call includes `"Además, me llegó un volante... precio mucho más bajo"`, but `kb-competitor-price-save` originally had English-only trigger coverage. I added Spanish trigger phrases/keywords plus `es-US` trigger locale so the demo utterance is no longer a lexical blind spot.
- **Explicit delivery scheduling/reschedule coverage was missing.** Added `kb-delivery-scheduling-window-request` so reps now have live-call-safe language for `can you schedule it for Friday morning`, access-note handling, and no-fake-ETA guidance.
- **Validation:** `dotnet test CallCenterTranscription.sln --nologo` passed before and after the data update (97 total, 94 pass, 3 skip, 0 fail). Secondary review agent verdict: approved, no blockers.

## Learnings — 2026-06-11T16:42:31.815-04:00 (Rep Accept Latency Review)

- **Verdict: APPROVE.** Meyrin's `EmitCallPendingAndTryAddRepAsync` fast-path is safe and correct. Dyakka's telephony analysis is sound.
- **Key safety invariant preserved:** `ActiveCallStore.RepAccepted` (set only by `AddParticipantSucceeded` callback) remains the sole gate for transcription emission in `SpeechTranscriptionService`. The early `AddParticipant` attempt shortens time-to-ring but cannot bypass the accept gate.
- **Stale-callback guard confirmed:** `IsCurrentActiveCall()` uses strict ordinal comparison of ACS call-connection IDs on both `AddParticipantSucceeded` and `AddParticipantFailed` handlers. A late-arriving callback from a previous call is silently dropped.
- **Double-invite prevention:** `callStore.RepAdded` check (line 276) prevents the fast-path from re-inviting if `CallConnected` reconverge already succeeded — and vice versa via `TryBeginAddRep()` CAS latch.
- **Test coverage:** 7 tests in `AcsEndpointsLatencyTests.cs` — ordering (pending before add), no-rep-registered path, fault resilience (add throws → pending still sent), and 4 `IsCurrentActiveCall` boundary cases (match, mismatch, empty active, empty callback).
- **Residual latency is ACS-internal:** After `AddParticipant` fires, the time for ACS to deliver `incomingCall` to the browser Calling SDK is platform-controlled and not reducible from our code. User's observed ~8s includes this irreducible portion.
- **Follow-up opportunity (non-blocking):** Reducing `/rep/register` heartbeat from 15s → 5s would shrink worst-case stale-registry delay when the fast-path fires before rep is registered.

## Learnings — 2026-06-11T16:49:37.710-04:00 (Scripted Demo Validation)

- **Verdict: APPROVE.** All three scripted demo conversations now prove the rep-facing knowledge path, not just raw transcript playback.
- **Coverage added:** `DemoScriptedScenarioFeedTests` now validates every expected trigger turn across all 3 scripts (`demo-missed-delivery-bilingual-save`, `demo-low-tank-auto-delivery-conversion`, `demo-renewal-rate-hardship-save`) and checks exact knowledge item IDs, rank ordering, snippet/guidance payload, citation label, source section/URL, and matched-evidence metadata.
- **Bilingual demo guard added:** The Spanish competitor-pricing turn is pinned to a translation-backed evidence assertion so the demo cannot silently regress to untranslated trigger metadata.
- **Real bug found during QA and closed:** the scripted feed could emit an extra assist card on later customer turns because the matcher could still latch onto same-script expectation text after the intended turn. The feed now only emits assist events for customer turns whose script metadata explicitly expects knowledge IDs, keeping demo playback deterministic.
- **Validation:** `dotnet test tests/CallCenterTranscription.Tests/CallCenterTranscription.Tests.csproj --nologo --no-restore --filter "DemoScriptedScenarioFeedTests|ReasoningClientTests"` passed (8/8). `dotnet test CallCenterTranscription.sln --nologo --no-restore` passed (116 total, 113 pass, 3 skip, 0 fail). Secondary review agent: no significant issues found.
