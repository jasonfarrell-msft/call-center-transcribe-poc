# Athrun — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **Goal:** Demonstrable web app. Fork live call audio via Azure Communication Services, run a pipeline (diarization → translation → continual sentiment → churn-risk agent → knowledge surfacing → next-step suggestions), render to an agent-assist UI.
- **Industry:** Propane retail. Churn = customer decides to stop buying propane from this company.
- **Constraints:** POC may be scripted; data/knowledge mocked to simplest case. Mid-tier models, **MAI preferred**, via latest GA Azure AI Foundry. Security: managed identity, no secrets in code.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Prior Decisions & Learnings (2026-06-05 to 2026-06-09)

**Architecture Pivot Summary:**
- 2026-06-05: TypeScript/Node stack → SUPERSEDED by C#/.NET 9 + ASP.NET Core + Razor Pages + SignalR
- Stack locked: Backend on ACA (WebSocket + SignalR), Frontend on App Service, Managed Identity everywhere
- Model: GPT-5.4 (5.3+ policy enforced) via Azure.AI.OpenAI / AI Foundry; MAI deferred until GA
- Solution: 5 projects (Api, Web, Shared, Ai, Telephony) + Tests
- 2026-06-06: Sweden Central deployment verified; ACS + Translator are geography-global (regional bloc confirmed acceptable)
- 2026-06-07 to 2026-06-08: Reviewed Lunamaria's token-based CSS redesign (APPROVED after WCAG AA fixes), mission-control screen-split, 80/20 layout
- 2026-06-08: Reviewed Lacus's SpeechTranscriptionService consumer (APPROVED; R1 regress verified; diarization deferred)
- 2026-06-08: Go-Live gate review (APPROVED after Meyrin fixes build errors)

## 2026-06-10 — Rep Call-Control & Customer-Only Sentiment Decision

**Shipped with Dyakka/Lacus/Lunamaria/Yzak (commit 17a18c0).**

**Customer-only sentiment architecture approved:**
- Rep voice excluded from churn meter (industry standard: Genesys, NICE, Amazon Connect all score per-speaker separately)
- Rep empathy phrases ("I'm so sorry") would score negative in lexicons; they indicate good service, not customer dissatisfaction
- POC compromise: Mixed audio sentiment OK for demo (customer ~70% of words, rep filler scores neutral, EMA α=0.4 dampens noise); label as Phase 2 spike
- Phase 2 spike: ConversationTranscriber on Mixed stream (diarized) + customer-only filter

**New lifecycle gate (`repAccepted`) approved:**
- CallPending (answer) → CallAccepted (AddParticipantSucceeded) → CallEnded (all disconnect paths)
- Decline now correctly → HangUp forEveryone (eliminates orphaned PSTN customer leg)
- Cross-module teardown via `rep.callEnded` CustomEvent guarantees audio stops

**Risk assessment passed:**
- R1 regression: None (same Mixed 16kHz stream, same AAD auth, ConversationTranscriber is constructor-compatible drop-in)
- Speaker attribution: Correct (customer latched from first non-Unknown speaker in `Transcribed`; rep empathy excluded from scoring)
- Lifecycle: All paths converge to same finally-block; no double-teardown
- Emission gate: Double-gated (server suppresses pre-accept; client gate backup)
- Security: /rep/hangup uses same X-Rep-Key auth as sibling endpoints

**Advisory (non-blocking):** Partial results (`Transcribing`) may briefly show rep attribution before first final; self-corrects <1s. Acceptable POC edge case.

**Test verdict:** 53 pass (16 PASS, 6 MANUAL-ONLY, 0 FAIL across 22 scenarios). Green.

## 2026-06-10 — Customer Hangup Teardown Review (Dyakka)

**Reviewed:** `ParticipantsUpdated` + `CallDisconnected` handlers, `TryBeginTeardown()` CAS claim, `_currentSession` volatile pattern.

**Verdict: ✅ APPROVE**

**Build/Test:** `dotnet build` 0 errors; `dotnet test` 56 passed, 3 skipped, 0 failed.

### Findings

1. **CORRECTNESS — PASS.** `Count > 0 && !Any(PhoneNumberIdentifier)` correctly detects PSTN departure. `callIdForPU != null` guard prevents firing before any call is active.

2. **RACE SAFETY — PASS with advisory.** `TryBeginTeardown()` CAS is sound. One narrow phantom re-win scenario: if WebSocket finally wins first and calls `Clear()`, `CallDisconnected` can re-win `TryBeginTeardown()`, reads null `callId`, and breaks WITHOUT calling `Clear()` — leaving `_teardownState` stuck at `TeardownInProgress`. Self-heals on next `CompleteIncomingClaim()`. No duplicate broadcast fires (null callId guard suppresses it). Non-blocking for POC.

3. **R1 CONSTRAINT — PASS.** `Mixed` + `Pcm16KMono` untouched. Sacred. ✓

4. **SECURITY — PASS (no regression).** Pre-existing AllowAnonymous webhook risk unchanged. Event Grid Entra delivery auth TODO remains a production prerequisite.

5. **RESOURCE LEAKS — PASS.** `EndMediaClaim()` always fires. Both teardown branches call `CompleteStream`. `_currentSession = null` before `TryComplete()` in `CompleteStream` prevents re-entry from `ForceCompleteCurrentSession`.

### Advisories (non-blocking)

- Advisory A: `ParticipantsUpdated` is not guaranteed to fire on all PSTN carriers for 2-party hangups; `CallDisconnected` is the authoritative path. Belt-and-suspenders design is correct. Document in Phase 2.
- Advisory B: Yzak to add unit test for `TryBeginTeardown()` double-win scenario.

**Lockout applied (N/A for APPROVE).**

## 2026-06-10 — Customer Hangup Teardown Code Review

**Reviewed Dyakka's teardown implementation (commit 173afea).**

**Verdict: APPROVE** with 2 non-blocking advisories for Phase 2:
- Advisory A: `ParticipantsUpdated` reliability varies by carrier; `CallDisconnected` is authoritative teardown signal (document in Phase 2 design)
- Advisory B: Add unit test for `TryBeginTeardown()` double-win scenario to pin contract (Yzak resolved)

**Findings:**
- Correctness: Sound for POC single-PSTN-party scenario
- Idempotency: CAS pattern correctly guarantees one teardown winner
- R1 constraint: Audio topology unchanged (Mixed, Pcm16KMono untouched)
- Security: No new attack surface (pre-existing Event Grid auth TODO remains)
- Resource leaks: None — all paths clean up correctly

**Build: 0 errors. Tests: 56 pass, 3 skip, 0 fail.**

## 2026-06-10 — Speaker Label Flip Fix Review (Lacus, commit cf3694e)

**Reviewed:** `SpeakerAttributionState.cs` (new), `SpeechTranscriptionService.cs` (updated), `SpeakerAttributionStateTests.cs` (14 new tests). Replaces single-slot "first-seen = customer" heuristic with phase-aware two-slot state machine gated on `ActiveCallStore.RepAccepted`.

**Verdict: ✅ APPROVE** with one non-blocking advisory.

**Build/Test:** `dotnet build` 0 errors, 0 warnings. `dotnet test` 75 passed, 3 skipped, 0 failed.

### Findings

1. **CORRECTNESS — PASS.** State transitions are sound for both main scenarios:
   - **Phase 1 (normal):** Customer speaks while on hold pre-accept → latched as Customer; rep speaks post-accept → latched as Rep (Phase 2A). ✅
   - **Phase 2B (the reported bug):** Customer silent on hold; rep greets first post-accept → correctly latched as Rep; customer responds → latched as Customer. The flip is fixed. ✅
   - **Residual edge (advisory, not blocking):** If the customer was completely silent pre-accept AND speaks first after accept (before the rep utters anything), Phase 2B incorrectly latches the customer as Rep. This scenario requires: (a) customer silent while on hold (no diarization events pre-accept), AND (b) customer speaks before the rep after accept. In a propane call center demo — especially on mock audio — the rep greeting first is the invariant. Not a blocking concern for the demo.

2. **ROBUSTNESS — PASS.** The PSTN "4:" / CommunicationUser "8:" identifier is available in ACS `ParticipantsUpdated` events but is NOT exposed through `ConversationTranscriptionResult.SpeakerId` — the Speech SDK returns opaque diarization cluster IDs ("Guest-1", etc.) with no native link to ACS participant identities. A deterministic identity mapping would require a non-trivial side-channel correlation table. For a POC, the phase-aware heuristic is the correct tradeoff. Lacus made the right call.

3. **SENTIMENT INTEGRITY — PASS.** `attribution.IsCustomer(speakerId)` is the gate for both `Transcribing` (partials) and `Transcribed` (finals) handlers. `IsCustomer()` returns true only for the latched `CustomerSpeakerId`. Rep utterances (different SpeakerId) are transcribed but `isCustomer = false` → excluded from sentiment pipeline. No regression to scoring.

4. **R1 CONSTRAINT — PASS.** `SpeakerAttributionState` is purely a text-level labeling layer. `MediaStreamingOptions` with `Mixed` + `Pcm16KMono` is untouched. Sacred. ✓

5. **TEST QUALITY — PASS with advisory.** 14 tests cover Phase 1, Phase 2A, Phase 2B (bug scenario), Phase 2B resolution, slot stability, Unknown handling, IsCustomer edge cases, IsSpeakerKnown theory test, and Observe return-value contract. The exact reported bug scenario (`Phase2B_RepSpeaksFirstPostAccept_IsLatchedAsRep`) is explicitly tested. **Missing:** no test for the residual edge case (customer speaks first post-accept, no pre-accept speech). Non-blocking.

### Advisory (non-blocking)
- **Advisory A:** Add one test `Phase2B_CustomerSpeaksFirstPostAccept_NoPreAcceptSpeech` to document the known residual flip scenario. It should assert that `CustomerSpeakerId` is incorrectly latched as null and `RepSpeakerId` is set to the customer's Guest ID — i.e., explicitly document the known limitation so it's visible if the heuristic is ever revisited. Assign to Yzak.

**Lockout applied (N/A — APPROVE).**

## 2026-06-10 — NBA/Churn UI Removal Review (Lunamaria, commit f3cccf0)

**Reviewed:** Removal of "Churn Risk" and "Next Best Action" cards from the agent-assist metadata column.

**Verdict: ✅ APPROVE**

**Build/Test:** `dotnet build` 0 errors, 0 warnings. `dotnet test` 76 passed, 3 skipped, 0 failed.

### Findings

1. **CORRECTNESS — PASS.** Both card sections fully removed from `Index.cshtml` (live-mode branch only). All churn/NBA DOM selector variables, `onChurnRisk()`, `onNextBestAction()`, and `stream.churnRisk`/`stream.nextBestAction` SignalR registrations removed from `live-transcript.js`. Dead CSS classes `.assist-kicker`, `.assist-copy`, `.assist-meta` cleaned up. No orphaned references remain in the Web project.

2. **SCOPE DISCIPLINE — PASS.** Sentiment gauge, transcript panel, and knowledge-article cards all intact. `SpeechTranscriptionService.cs` and backend pipeline untouched — backend still fires `stream.churnRisk` and `stream.nextBestAction`; the UI simply no longer listens. Correct approach for a UI-only change.

3. **NO BROKEN REFERENCES — PASS.** `grep` over `src/CallCenterTranscription.Web/` found zero remaining references to removed DOM IDs or functions. Remaining references in `tests/` (`ApiWiringSmokeTests`, `PipelineReplayPublisherTests`) are for the backend API routes `/api/events/churn-risk` and `/api/events/next-best-action` — these are live API tests for the intact pipeline, not dead UI refs.

4. **TESTS — PASS.** Build 0 errors. 76 pass, 3 skip, 0 fail. `WebConsoleTests` assertions for `data-live-churn-panel` and `data-live-nba-panel` correctly removed.

5. **SECURITY — PASS.** Trivial UI removal; no new attack surface.

### Advisory (non-blocking)
- `site.css:555` contains stale comment: `/* Side column: scrollable so future stacked panels (knowledge cards, churn, etc.) work */`. Cosmetic only — update at leisure.

**Lockout applied (N/A — APPROVE).**


## Learnings

### 2026-06-11 — Next knowledge slice should prove runtime wiring, not future RAG
- The new `synthetic-agent-assist-knowledge.v1.jsonl` corpus is currently a data artifact only; the runtime still retrieves from legacy embedded `synthetic-knowledge.v1.json` via `SyntheticCorpusLoader` → `KiraContentPack`.
- The shortest honest POC path is to bridge the new JSONL into the existing `KnowledgeCardEvent`/SignalR/UI flow rather than introduce Azure AI Search before the data is visible in-call.
- Acceptance for this slice must include deterministic utterance-to-record mapping and at least one negative case; otherwise a default fallback card can create a false sense that retrieval works.

### 2026-06-10 — Architect README framing
- Top-level README should be treated as an **Explanation** document for architects/reviewers, not a setup guide or API reference.
- The architecture view should center on the Azure runtime split: Web on App Service, API on Container Apps, ACS/Event Grid ingress, Speech/Translator/Foundry downstream, and SignalR fan-out from the API.
- Call out that SignalR is **application-hosted**, not Azure SignalR Service, so reviewers do not infer a managed realtime service that does not exist.
- Put POC boundaries near the top or immediately after the system overview so the document does not overstate production readiness.
- A root architecture README for reviewers should stay at the system-topology level: explain Azure ingress, runtime placement, and critical-path caveats without drifting into dev setup or endpoint-by-endpoint reference.
