# Session Log: Speaker Label Fix

**Date:** 2026-06-10T15:20:14Z  
**Agents:** Lacus (AI Engineer), Athrun (Lead), Yzak (QA)  
**Status:** COMPLETE

## Outcome

✅ Fixed persistent speaker label inversion. Rep and Customer labels now correctly attributed via phase-aware two-slot mapping in `SpeakerAttributionState`.

## Scope

- Root cause analysis: diarization cluster ordering, not call arrival order
- Implementation: phase-aware state machine gated on `RepAccepted`
- Coverage: 14 unit tests + 1 documentation test pinning residual edge
- Build: 0 errors, 0 warnings; 75 tests pass

## Key Decision

Replace first-seen heuristic with phase boundary (`RepAccepted`):
- Phase 1 (pre-accept): any speaker = customer
- Phase 2A (post-accept, customer latched): new speaker = rep
- Phase 2B (post-accept, neither latched): first speaker = rep, second = customer

## Known Limitation

Phase 2B edge: customer speaks first post-accept with no pre-accept speech → labels remain inverted. Demo-unlikely; documented by test. Future fix requires ACS participant metadata or timing heuristic (Phase 2).

## Commit

cf3694e (pushed to origin/main)

