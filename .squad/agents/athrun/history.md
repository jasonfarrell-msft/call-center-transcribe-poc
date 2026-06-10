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

