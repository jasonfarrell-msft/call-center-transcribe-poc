# Dyakka Run 1: ACS Assessment + Plan

**Date:** 2026-06-08T14:05:26Z  
**Agent:** Dyakka (ACS/Telephony Specialist)  
**Phase:** Discovery & Planning  
**Deliverable:** `dyakka-acs-assessment-and-plan.md`

## Summary

Completed comprehensive assessment of ACS state:
- Identified existing: ACS resource (Europe dataLocation), IAudioSource contract, MockAudioSource registered, ACA ingress ready
- Identified gaps: AcsAudioSource not implemented, IncomingCall webhook missing, WebSocket route missing, Event Grid deferred, RBAC not assigned, minReplicas at 0
- Presented 4 phased implementation plan (Infra prereqs, API routes, AcsAudioSource, Audio pipeline coordination)
- Proposed 3 options: A (full PSTN), B (ACS web call), C (plumbing + mock) with strong recommendation for Option C as default

## Key Decisions Needed from Jason

1. Buy Swedish PSTN number?
2. Real inbound vs ACS web call?
3. How live should the demo be?
4. minReplicas as permanent or manual CLI?
5. Event Grid webhook security method?
6. ACS RBAC role to assign?

Status: **Inbox — awaiting Jason's steering decisions**
