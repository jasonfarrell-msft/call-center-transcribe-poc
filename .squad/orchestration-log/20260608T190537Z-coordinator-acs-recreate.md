# COORDINATOR — ACS Recreate (US Data Residency) + RBAC Apply
**Timestamp:** 2026-06-08T19:05:37Z
**Status:** LIVE

## ACS Deletion & Recreation
**Old Resource:** acs-cctrans-kdarok (Europe dataLocation)
- Deleted via `az communication delete`
- No Event Grid, no phone number, no sunk assets

**New Resource:** acs-cctrans-kdarok (United States dataLocation)
- Created via `az communication create --data-location "United States"`
- Host name: acs-cctrans-kdarok.unitedstates.communication.azure.com
- Provisioning: Succeeded
- ACA Acs__Endpoint env var: unchanged (name-based generic endpoint remains valid)

## RBAC Assignment (Live-Applied)
- Principal: ACA system MI (6edcf409-903a-49ec-ae48-aed391da1fa7)
- Role: Communication and Email Service Owner (09976791-48a7-449e-bb21-39d1a415f350)
- Resource: acs-cctrans-kdarok (ACS)
- Command: `az role assignment create`
- Status: Present & verified

## Provisioning Notes
- azd provision: NOT used (environment bare, unsafe for full provision)
- Used surgical az-CLI commands instead, consistent with committed Bicep
- Next full `azd provision` should succeed with corrected Bicep in place
