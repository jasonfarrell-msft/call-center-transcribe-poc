# Athrun Run 2: dataLocation EU → US Flip Reviewer Gate

**Date:** 2026-06-08T18:49:06Z  
**Agent:** Athrun (Lead / Architect)  
**Phase:** Review gate — dataLocation parameter flip  
**Deliverable:** `athrun-acs-datalocation-review.md`

## Summary

Reviewed Meyrin's infra change (dataLocation `'Europe'` → `'United States'`) and issued **✅ APPROVE**.

### Verification Completed

1. **Effective dataLocation Reaching ACS:** `'United States'` ✅
   - infra/main.bicep param default: `'United States'`
   - infra/main.parameters.json authoritative value: `'United States'`
   - No stale `'Europe'` override
   - Correct casing

2. **RBAC Determinism on Recreate:** ✅
   - Module uses deterministic `guid(communicationServicesAccount.id, principalId, roleDefinitionId)`
   - Scoped to resource (not RG/subscription)
   - Auto-reapplies to new resource on next provision
   - Zero manual RBAC work needed

3. **AudioSource__Mode:** ✅
   - Still `'Mock'` (unchanged)
   - Live ACS activation remains deferred

4. **Scope & Drift:** ✅
   - Changed: 9-line comment block + value flip (2 files)
   - No other parameters modified
   - **Zero secrets introduced** (grep verified)
   - No unrelated changes

5. **Bicep Compilation:** ✅
   - `az bicep build` → 0 errors, 0 warnings
   - Generated `infra/main.json` successfully

6. **Operator Documentation:** ✅
   - Immutability note captured in code comments
   - Operator steps documented (delete resource, run azd provision, verify endpoint)
   - No risk of missed delete step or failed in-place update

### Key Facts

- `dataLocation` is immutable; in-place update fails
- Current ACS has no sunk assets (no phone number, no Event Grid)
- Ideal time to switch; safe delete + recreate
- RBAC automatically re-applies to new resource

**Status:** Ready for operator to delete existing ACS and run `azd provision`
