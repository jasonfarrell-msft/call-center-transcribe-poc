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

## Learnings Summary — 2026-06-10 through 2026-06-11T15:36:11-04:00

- **Rep call-control QA arc:** flagged the initial pending-badge and customer-only sentiment seam as real gaps, then approved the implemented `stream.callPending` path, the `SpeechTranscriptionService` customer-only sentiment filter, the `RepAccepted` accept gate, and the corrected `Clear()` → `EndMediaClaim()` production-order test coverage (56 total tests, 53 pass, 3 skip, 0 fail at that checkpoint).
- **Teardown idempotency:** added and validated the 3 `TryBeginTeardown()` regression tests proving first-call win, subsequent-call no-op behavior, and reset-on-`Clear()` for the next call (59 total tests, 56 pass, 3 skip, 0 fail).
- **Speaker-attribution edge cases:** first pinned the Phase-2B “customer speaks first post-accept with no pre-accept speech” limitation with a green documentation test, then approved Lacus's caller-order fix that latches the first known speaker as customer and the second as rep, while preserving the documented rep-first residual limitation outside the demo flow.
- **Synthetic demo-data review:** approved the knowledge-corpus refresh after adding Spanish competitor-pricing trigger coverage and a new delivery-scheduling/window guidance card; solution tests stayed green at 97 total, 94 pass, 3 skip, 0 fail.

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

## Learnings — 2026-06-12T10:50:28.555-04:00 (Demo Feedback Regression Resume)

- **Verdict: APPROVE.** The pending/Accept path is now correctly split: `/api/calls/active` exposes `pending` + `acceptAvailable=true` before any transcript exists, `PipelineReplayPublisher` replays `stream.callPending` without transcript history for pending calls, and the live UI keeps the lower badge on speech-service status (`Speech services connected`) while the Accept affordance lives in the top rep call bar.
- **Regression coverage tightened for the accepted path.** Added `ApiHost_LiveMode_ExposesAcceptedCallStateWithoutReopeningAccept` so the API now proves `repAccepted=true`, `acceptAvailable=false`, `state=accepted`, empty transcript history, and the accepted-session note before speech arrives.
- **Low-tank sentiment now demonstrates mid-call recovery instead of a late-only rescue.** Tightened `Feed_LowTankConversion_UpdatesSentimentBeforeFinalAcceptanceAndEndsResolved` to require an improving sentiment event at turn 4, a positive pre-close event at turn 6, and a resolved summary after the final customer acceptance at turn 7. Observed scripted scores: seq 4 = `-0.621` / improving, seq 6 = `0.420` / positive, final summary = `resolved`.
- **Full validation:** `dotnet test tests/CallCenterTranscription.Tests/CallCenterTranscription.Tests.csproj --nologo --no-restore --filter "ApiHost_LiveMode_ExposesAcceptedCallStateWithoutReopeningAccept|ApiHost_LiveMode_ExposesPendingCallStateBeforeAnyTranscript|Feed_LowTankConversion_UpdatesSentimentBeforeFinalAcceptanceAndEndsResolved|Store_RepResolutionCue_PublishesEarlierRecoverySignal|Store_FinalResolutionOverridesEarlyNegativeWhenCustomerAccepts|ReplayForCallAsync_WhenSnapshotIsPending_ReplaysPendingLifecycleBeforeTranscript|Homepage_WhenLiveModeEnabled_RendersSignalrDrivenAssistPanels"` passed (7/7). `dotnet test CallCenterTranscription.sln --nologo --no-restore` passed (123 total, 120 pass, 3 skip, 0 fail).
- **Secondary review note:** independent reviewer re-raised the known live ACS rep-first post-accept diarization ambiguity. I am NOT blocking this demo-feedback gate on it because it is already documented as a known limitation outside the scripted demo flow and was not introduced by these changes.
