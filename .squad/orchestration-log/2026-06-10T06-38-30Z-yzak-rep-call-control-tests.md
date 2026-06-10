# Yzak — Rep Call-Control Test Scenarios & Verification

**Date:** 2026-06-10T06:38:30Z  
**Author:** Yzak (Tester / QA)  
**Status:** READY FOR IMPLEMENTER REVIEW  
**Scope:** Ring→accept/reject→live transcript→teardown test scenarios + edge cases

## Summary

**22 comprehensive test scenarios** derived from Athrun's architecture decision and cross-checked against `ActiveCallStore` state machine, `LiveSentimentStore` state machine, `rep-phone.js` softphone state machine, and `live-transcript.js` badge/conn-status machine.

**Test format breakdown:**
- 🤖 Automatable (unit tests, xUnit stubs in `RepCallControlTests.cs`)
- 🛠 Integration (needs running API + SignalR hub client)
- 👁 Manual (requires live ACS, real phone, browser)
- ⚠️ Implementation gap flagged (production code required)

## Known Gap — "Call Pending" Badge State (§1)

**Issue:** Badge state machine has `disconnected`, `connecting`, `live`, `ended` states. No `pending` state during ring phase.

**Current:** `rep-phone.js` shows Accept/Decline but does NOT communicate ringing state to `live-transcript.js`.

**Required:** Either (1) API emits `stream.callIncoming` SignalR event when `IncomingCall` webhook fires, OR (2) `rep-phone.js` fires local `CustomEvent` that `live-transcript.js` listens to.

**Directive:** Do NOT implement "Call Pending" badge until approach is chosen by Lacus/Athrun. Test scenario TC-02 marked ⚠️ until then.

## Scenario Coverage

**Badge state machine:** TC-01 (Idle), TC-02 (Ringing→Pending)⚠️, TC-03 (Accept→Connected), TC-04 (Reject→Disconnected), TC-05 (Customer hangup→teardown), TC-06 (Rep hangup→teardown), TC-07 (Disconnect during ended-timer).

**Reject path:** TC-08 (No sentiment from rejected call), TC-09 (No ghost line after reject), TC-10 (ActiveCallStore clean after reject).

**Accept path:** TC-11 (Sentiment receives only customer utterances), TC-12 (Sentiment state clean on every Accept), TC-13 (Transcript lines only after Accept).

**Teardown:** TC-14 (Late Speech SDK utterances dropped), TC-15 (ActiveCallStore fully reset), TC-16 (MediaClaim released), TC-17 (No transcript events after Clear).

**Edge cases:** TC-18 (Double incoming call rejected), TC-19 (Accept after caller hung up), TC-20 (Reject then immediate new call), TC-21 (Browser refresh mid-ring), TC-22 (Browser refresh mid-call).

**Live demo checklist:** Pre-flight, Ring phase, Accept, Live transcription, Customer hangup, Rep-initiated hangup, Post-teardown regression.

## xUnit Test Stubs

Stubs provided in `tests/CallCenterTranscription.Tests/RepCallControlTests.cs`:
- Automatable scenarios (TC-01, TC-03, TC-04, TC-07, TC-08, TC-10, TC-12, TC-14, TC-15, TC-16, TC-18) compile and run.
- Gap-dependent scenarios (TC-02, TC-11, TC-13) marked `[Fact(Skip = ...)]` pending implementation decisions.
- Manual scenarios (TC-05, TC-06, TC-09, TC-19, TC-21, TC-22) documented for ad-hoc verification.

## Open Questions for Implementers

| # | Question | Owner |
|-|-|-|
| Q1 | "Call Pending" badge: Option 1 (SignalR `stream.callIncoming`) vs Option 2 (local `CustomEvent`)? | Athrun / Lacus |
| Q2 | Customer-only sentiment: filtering done in `SpeechTranscriptionService` or at call site? | Lacus |
| Q3 | `Reset()` timing: on ring start or media stream begin (after Accept)? | Lacus |
| Q4 | Late transcript post-hangup race (TC-17): frontend `isCallActive` flag needed or backend guarantees no events after `stream.callEnded`? | Lacus / Athrun |

---

*Orchestration entry: yzak role authored 22 test scenarios with full edge-case coverage, identified 1 blocking implementation gap, provided xUnit stubs for 10 automatable scenarios, and documented complete live demo verification checklist. Ready for implementation team.*
