# Session: Rep Call-Control Lifecycle & Customer-Only Sentiment

**Date:** 2026-06-10T06:38:30Z  
**Participants:** Athrun, Yzak  
**Outcome:** Determination + Implementation plan + 22 test scenarios

## Brief Summary

**Athrun produced:**
- Accuracy review: Customer-only sentiment is correct (retention churn = customer behavior, not rep tone)
- Architecture decision: Mixed audio stays (proven, stable); customer-only filtering deferred to Phase 2 diarization spike
- Implementation plan: 6 tasks across Lunamaria (UI badge states + transcript gating), Meyrin (repAccepted event), Dyakka (decline→HangUp teardown)
- Risk callouts: Timeout fallback if repAccepted never fires; generous AddParticipant timeout (60s) for slow reps

**Yzak produced:**
- 22 comprehensive test scenarios (🤖 unit, 🛠 integration, 👁 manual, ⚠️ gaps)
- Badge state machine: idle → ringing→"Call Pending" → accept→"Connected" → hangup→"Disconnected"
- Reject path: no sentiment leak, no ghost lines, clean store state
- Accept path: customer-only sentiment (pending Q2 clarification), clean resets
- Teardown: late utterance drops, full state reset, media claim release
- Edge cases: double-incoming (auto-reject), mid-ring caller hangup, rapid succession, browser refresh
- xUnit stubs: 10 automatable scenarios; 3 gap-dependent (TC-02, TC-11, TC-13) marked skip-pending
- Live demo checklist: pre-flight, ring, accept, live transcription, hangup variants

## Next

Implementation phase: Task 3 (Meyrin) → Task 4 (Dyakka) → Tasks 1+2+5 (Lunamaria). Resolve Q1 (badge approach) before TC-02 xUnit execution.

---

*Logged:* 2026-06-10T06:38:30Z
