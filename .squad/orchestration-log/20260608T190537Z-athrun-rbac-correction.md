# athrun — ACS RBAC Role Correction
**Timestamp:** 2026-06-08T19:05:37Z
**Status:** COMPLETED

## Decision
Revised ACS RBAC role assignment from "Communication Services Contributor" (GUID 2b4609a5-7812-4aba-b5e3-076e6a078419) — which does not exist in this directory — to "Communication and Email Service Owner" (GUID 09976791-48a7-449e-bb21-39d1a415f350).

**Rationale:**
- Original role GUID not present (az role definition list showed 0 matches)
- "Communication and Email Service Owner" is the only available built-in ACS role
- Broader than ideal (includes Email + ListKeys), but POC-acceptable resource-scoped assignment
- Applied to ACA system MI (principal 6edcf409-903a-49ec-ae48-aed391da1fa7)

## Scope
- Resource: ACS acs-cctrans-kdarok
- Principal: ACA system MI
- Role: Communication and Email Service Owner (09976791-48a7-449e-bb21-39d1a415f350)
- Scoped to ACS resource only

## Sign-off
This decision supersedes the "Communication Services Contributor" role specified in the Option C sign-off due to unavailability in the directory.
