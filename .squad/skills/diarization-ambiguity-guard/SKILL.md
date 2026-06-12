# Skill: Diarization Ambiguity Guard for Customer-Only Sentiment

**Domain:** Real-time transcription / sentiment routing
**Last updated:** 2026-06-11T16:42:31.815-04:00
**Author:** Lacus

---

## Problem This Solves

Conversation diarization can emit new `SpeakerId` values mid-call for the same human speaker. If these new IDs are guessed via turn-taking, rep speech can be mislabeled as customer and corrupt customer-only sentiment.

## Pattern

1. Latch first two distinct known speakers deterministically for inbound flow:
   - first = `Customer`
   - second = `Rep`
2. After both slots are latched, treat any additional speaker IDs as **ambiguous**.
3. Do **not** route ambiguous IDs into customer sentiment.

## Why This Pattern

In retention/churn workflows, false confidence is worse than a conservative no-update. Ambiguity guard preserves trust in sentiment/churn outputs by preventing known misrouting modes.

## Test Requirements

- Assert third/new post-latch speaker IDs are not customer.
- Assert customer sentiment does not move when only ambiguous IDs appear.
- Keep baseline tests for first-speaker and second-speaker latching.

## References

- `src/CallCenterTranscription.Api/Services/SpeakerAttributionState.cs`
- `tests/CallCenterTranscription.Tests/SpeakerAttributionStateTests.cs`
- `tests/CallCenterTranscription.Tests/LiveSentimentTests.cs`
