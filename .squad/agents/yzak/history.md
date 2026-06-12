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

## Learnings — 2026-06-12T13:16:06.171-04:00 (Accept Answerability Regression Gate)

- **Verdict: REJECT.** Added source-contract regressions proving the live softphone only enables Accept after a real ACS `incomingCall` handle exists and that the lower badge stays on speech-service status during `stream.callPending`.
- **New blocker captured:** `live-transcript.js` `resync()` still treats any `/api/calls/active` payload with a `callId` as pending, ignoring `acceptAvailable`, `repAccepted`, and `state`. That can reopen a misleading pending/Accept UI after reload on an already-accepted live call or when live frontend points at mock/scripted state.
- **Evidence:** `dotnet test tests/CallCenterTranscription.Tests/CallCenterTranscription.Tests.csproj --nologo --no-restore --filter "RepCallControlTests"` now fails in `Frontend_Resync_GatesPendingOfferOnAnswerableActiveCallState` and `Frontend_Resync_DoesNotReopenAcceptedOrMockCallsAsPending`, while the focused non-blocking regressions previously passed for the pending/accepted API contracts.
- **Required follow-up:** Meyrin/Lunamaria should revise the frontend resync contract to gate pending UI on authoritative answerability (`acceptAvailable` and/or accepted state handling), then rerun the new QA regressions before requesting approval.

## Learnings — 2026-06-12T13:16:06.171-04:00 (Accept Answerability Re-review)

- **Verdict: APPROVE.** The revised `live-transcript.js` `resync()` now fails closed: it reopens pending only when `/api/calls/active` says `acceptAvailable=true`, and it only restores an accepted call after `/api/session/current-state` confirms the same non-mock `callId` in `accepted`/`active`/`live` state.
- **Original blocker is closed.** The old `if (data && data.callId)` reopen path is gone, so accepted live calls and mock/scripted snapshots no longer regress into a fake pending/Accept state on reload or reconnect.
- **Validation:** `dotnet test tests/CallCenterTranscription.Tests/CallCenterTranscription.Tests.csproj --nologo --no-restore --filter "RepCallControlTests|ApiHost_LiveMode_ExposesPendingCallStateBeforeAnyTranscript|ApiHost_LiveMode_ExposesAcceptedCallStateWithoutReopeningAccept|ApiHost_LiveMode_ExposesAcceptedCallStateEvenIfPendingWasMissed|ApiHost_MockMode_DoesNotExposeSyntheticPendingAcceptState|EmitCallPendingAsync_EmitsPendingEvent|IsCurrentActiveCall_MatchesOnlyExactActiveCallId"` passed (32 total, 30 pass, 2 skip, 0 fail). `dotnet test CallCenterTranscription.sln --nologo --no-restore` passed (133 total, 131 pass, 2 skip, 0 fail).
- **QA learning:** for live-call resync, `/api/calls/active` alone is not a safe source of truth for accepted recovery; pair it with `/api/session/current-state` call-id + non-mock + state agreement before dispatching accepted UI.

## Learnings — 2026-06-12T14:14:36.351-04:00 (Release build review)

- **Verdict: APPROVE.** Meyrin's Dockerfile fix is the right deployment-build correction for the post-accept-state break.
- **Root cause reproduced without Docker:** `CallCenterTranscription.Ai.csproj` embeds `../../samples/agent-assist-demo-scripts.v1.json` and `../../samples/agent-assist-demo-trigger-expectations.v1.json`; a source-only publish context fails with `CSC : error CS1566` because those files are absent.
- **Fix path validated:** simulating the old Docker context (`src/` only) reproduced the missing-resource publish failure, and simulating the new context (`src/` + `samples/`) made `dotnet publish src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj -c Release --nologo` succeed.
- **Release validation:** `dotnet build CallCenterTranscription.sln -c Release --nologo` succeeded and `dotnet test CallCenterTranscription.sln -c Release --nologo --no-restore` passed (133 total, 131 pass, 2 skip, 0 fail).
- **Caveat:** Docker CLI is unavailable in this QA environment, so the final release gate still needs one real `docker build -f src/CallCenterTranscription.Api/Dockerfile .` run in CI or another Docker-capable environment.
