# meyrin — Bicep ACS RBAC GUID Fix
**Timestamp:** 2026-06-08T19:05:37Z
**Status:** COMPLETED & COMMITTED

## Decision
Updated infra/main.bicep to correct ACS RBAC role GUID and variable naming.

**Changes:**
- Line ~87: `var communicationServicesContributorRoleDefinitionId` → `var communicationServiceOwnerRoleDefinitionId`
- Line ~87: GUID `2b4609a5-7812-4aba-b5e3-076e6a078419` → `09976791-48a7-449e-bb21-39d1a415f350`
- Line ~461: Updated role variable reference to use corrected name
- Updated comment to reflect corrected role

## Validation
- `bicep build` returned 0 errors
- Committed to main branch

## Alignment
Aligns with athrun's corrected RBAC decision; ensures Bicep provisions same role on next run.
