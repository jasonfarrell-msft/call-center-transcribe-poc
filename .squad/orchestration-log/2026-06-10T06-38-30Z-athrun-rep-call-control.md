# Athrun — Rep Call-Control Lifecycle & Sentiment

**Date:** 2026-06-10T06:38:30Z  
**Author:** Athrun (Lead/Architect)  
**Requested by:** Jason  
**Status:** DETERMINATION + IMPLEMENTATION PLAN  
**Baseline tag:** R1_06.10.2026 (commit 4abce51) — Mixed audio transcription working

## Summary

**A. Accuracy Review:** Customer-only sentiment is CORRECT for propane retention. Rep voice is noise; empathetic words score negative (false negatives), enthusiastic words score false-positive. Rep coaching is different product surface — out of POC scope.

**B. Architecture Decision:** Mixed audio STAYS (proven, stable). Customer-only filtering at audio level needs diarization (Phase 2 spike with `ConversationTranscriber`). Pragmatic POC choice: score all Mixed utterances; customer words dominate in retention calls; rep filler scores neutral; EMA dampens noise.

**C. Implementation Plan:** New `repAccepted` SignalR event (fired on `AddParticipantSucceeded`) gates transcript visibility. Before rep accepts → "Call Pending" badge; after → green "Connected". Decline → HangUp teardown.

**Task breakdown:**
- Task 1+5 (Lunamaria): Badge states + transcript gating + callStarted→"Call Pending"
- Task 2 (Lunamaria): Rep-phone decline→teardown
- Task 3 (Meyrin): `repAccepted` event broadcast in AcsEndpoints.cs
- Task 4 (Dyakka): `AddParticipantFailed`→HangUp on same file (rebases on Task 3)
- Task 6: Sentiment — no changes (all utterances OK for POC)

**Merge order:** Task 3 (small, additive) → Task 4 (rebases) → Tasks 1+2+5 (consume new event).

**Risk mitigation:** 30-second timeout if `repAccepted` never arrives (fallback to showing transcript anyway). Generous AddParticipant timeout (60s) so slow reps aren't treated as declines.

**Verification:** End-to-end live call test: customer dials → backend answers → rep sees "Call Pending" → rep clicks Accept → badge→green "Connected" → transcript lines begin → sentiment scores customer words only → customer hangs up → badge→"Disconnected".

**File:** `.squad/decisions/inbox/athrun-rep-call-control.md` → merged to decisions.md

---

*Orchestration entry: athrun role owned the accuracy determination, architecture decision, task breakdown, and risk callouts. Implementation delegated to Lunamaria, Meyrin, and Dyakka.*
