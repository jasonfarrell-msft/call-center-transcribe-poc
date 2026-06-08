# Session: ACS Recreate (US Data Residency) + RBAC Correction
**Date:** 2026-06-08T19:05:37Z
**Session ID:** 20260608T190537Z-acs-recreate-us-and-rbac-fix

## LIVE STATE
✅ **ACS Deleted & Recreated**
- Old ACS (Europe): Removed. No Event Grid, phone numbers, or sunk assets.
- New ACS: acs-cctrans-kdarok (United States dataLocation)
- Host: acs-cctrans-kdarok.unitedstates.communication.azure.com
- Status: Provisioning Succeeded
- Endpoint env var (Acs__Endpoint): Unchanged (generic name-based endpoint still valid)

✅ **RBAC Applied (Live)**
- Principal: ACA system MI (6edcf409-903a-49ec-ae48-aed391da1fa7)
- Role: Communication and Email Service Owner (09976791-48a7-449e-bb21-39d1a415f350)
- Scoped to ACS resource
- Verified present via az CLI

✅ **Bicep Corrected & Committed**
- GUID updated (2b4609a5... → 09976791...)
- Variable renamed (CommunicationServicesContributor → CommunicationServiceOwner)
- bicep build: 0 errors
- Committed to main branch

## CORRECTED OPERATOR NEXT STEPS

### Phase 1: US Toll-Free Acquisition
- [ ] **(a)** Check subscription eligibility for US toll-free in portal
  - Navigate to ACS → Phone Numbers → Get
  - Verify eligibility for toll-free numbers in US
- [ ] **(b)** Acquire US toll-free number
  - Order toll-free number via ACS Phone Numbers UI
  - Configure SIP routing if required

### Phase 2: Event Grid & Audio Delivery (Lacus + Meyrin ownership)
- [ ] **(c)** Implement Event Grid subscription for ACS events (call lifecycle, DTMF, etc.)
- [ ] **(c)** Implement Entra delivery auth (token refresh + flow)
- [ ] **(c)** Implement audio→Speech consumer (Speech Service integration)
  - Coordinate with Lacus + Meyrin for design & implementation

### Phase 3: Go-Live
- [ ] **(d)** Flip `AudioSource__Mode=Acs` to enable ACS audio live
  - Prerequisite: Event Grid + Entra delivery auth + audio consumer ready

## DEPLOYMENT NOTES

**azd Environment Status:**
- Current env: Bare state (only AZURE_ENV_NAME set)
- Full `azd provision` unsafe until env reconstructed
- **Workaround used:** Surgical az-CLI commands (az communication create, az role assignment create)
- **Next time:** Either reconstruct env with `azd env new` or continue surgical az-CLI
- **Once ready:** `azd provision` will apply corrected Bicep successfully

**Bicep is now correct and committed.** Next provision cycle will apply the updated GUID & role automatically.

## Context
- Original ACS role decision ("Communication Services Contributor") was unavailable in directory
- athrun revised decision to "Communication and Email Service Owner" (only available ACS role)
- meyrin updated Bicep to match
- Coordinator applied live
- This session logs the completed handoff to operators for next phases (toll-free, Event Grid, audio consumer)
