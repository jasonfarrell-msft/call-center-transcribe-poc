# Decision: Speaker Label Fix — Two-Slot Phase-Aware Attribution

**Date:** 2026-06-10T11:20:14-04:00  
**Author:** Lacus  
**Status:** Implemented (commit cf3694e)  
**Supersedes:** `lacus-conversationtranscriber-impl` heuristic (first-seen = customer)

---

## Problem

Jason (live-tested) reported Rep and Customer labels were consistently flipped. Transcription was working, but:
- Speech labeled **Customer** was actually the **Rep**
- Speech labeled **Rep** was actually the **Customer**
- Customer-only sentiment was therefore scoring the rep's audio, not the customer's

## Root Cause

`ConversationTranscriber` assigns opaque Guest IDs (`Guest-1`, `Guest-2`, `Unknown`) by diarization cluster, **NOT chronological arrival order**. The previous heuristic latched the first non-Unknown SpeakerId from a `Transcribed` event as the customer.

This fails consistently in the common call flow:
1. Customer calls, is silent on hold
2. Rep accepts invite quickly (5–15 s)
3. **Rep says "Hello, [Company], how can I help?" — first complete `Transcribed` result**
4. Rep's SpeakerId latched as Customer → all labels inverted

The assumption "customer is on the stream before the rep, so first speaker = customer" was correct in theory but wrong in practice: the **first COMPLETE utterance** (not first audio presence) determines what gets latched, and the rep's greeting is typically that first utterance.

## Fix: Two-Slot Phase-Aware Attribution

`RepAccepted` (set by `MarkAccepted()` on `AddParticipantSucceeded`) is used as the authoritative phase boundary.

| Phase | Condition | Action |
|-------|-----------|--------|
| **1 — Pre-accept** | `RepAccepted = false` | Rep physically absent from Mixed stream → any speaker = **Customer** (definitive) |
| **2A — Post-accept, customer already latched** | Customer slot set, rep slot null, new speaker | New distinct speaker = **Rep** |
| **2B — Post-accept, neither latched** | Both slots null, `RepAccepted = true` | First speaker = **Rep** (greeting); second distinct speaker = **Customer** |

### Key implementation details

- Extracted to `SpeakerAttributionState` (internal sealed class) for testability — ConversationTranscriber is sealed in the SDK so the state machine must be tested separately.
- `IsCustomer(speakerId)` replaces the old `IsCustomerSpeaker(speakerId, customerSpeakerId)` static helper.
- Slots are **write-once** per call session. No flip after resolution.
- `InternalsVisibleTo` added to Api project to expose `SpeakerAttributionState` to the test project.
- 14 unit tests added (`SpeakerAttributionStateTests.cs`), including `Phase2B_RepSpeaksFirstPostAccept_IsLatchedAsRep` which directly encodes the flip scenario.

## Audio Topology

**Unchanged.** Mixed + Pcm16KMono (R1 sacred). This is labeling logic only, inside the `Transcribed` event handler.

## Customer-Only Sentiment

Unchanged in structure. `_liveSentiment.Append(...)` is still gated on `attribution.IsCustomer(speakerId)`. The fix ensures `IsCustomer` now returns `true` for the **actual customer**, not the rep.

## Files Changed

| File | Change |
|------|--------|
| `src/…/Services/SpeakerAttributionState.cs` | New — testable state machine |
| `src/…/Services/SpeechTranscriptionService.cs` | Uses SpeakerAttributionState; old static helpers removed |
| `src/…/CallCenterTranscription.Api.csproj` | InternalsVisibleTo test project |
| `tests/…/SpeakerAttributionStateTests.cs` | 14 unit tests |

## Mapping Rule Going Forward

> **Customer = first non-Unknown speaker seen PRE-accept, OR second distinct speaker seen POST-accept.**  
> **Rep = first distinct speaker seen POST-accept if neither was heard pre-accept, OR second distinct speaker if customer was latched first.**

For production, replace with deterministic ACS participant identity mapping (Unmixed audio or ACS participant role API). POC heuristic remains pragmatic and correct for the rep-greets-first call flow.
