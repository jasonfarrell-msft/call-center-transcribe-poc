# Meyrin Run 2: ACS dataLocation Europe → United States Flip

**Date:** 2026-06-08T18:49:06Z  
**Agent:** Meyrin (Backend Dev)  
**Phase:** Infrastructure update — dataLocation immutable-property recreate  
**Deliverable:** `meyrin-acs-datalocation-us.md`

## Summary

Implemented dataLocation switch from `'Europe'` to `'United States'` per Jason's request and Dyakka's feasibility advisory.

### Changes Made

1. **infra/main.bicep**
   - Line 12–18: Added 7-line comment block explaining immutability constraint and recreate-on-provision requirement
   - Line 19: param default changed `'Europe'` → `'United States'`
   - Rest of file: Bicep passes param through to resource definition (no change needed)

2. **infra/main.parameters.json**
   - Line 15: `communicationDataLocation.value` changed `"Europe"` → `"United States"`
   - Parameters file is authoritative; wins at provision time

3. **What's Unchanged**
   - ACS `location`: stays `'global'` (unrelated to dataLocation)
   - ACA compute region: stays Sweden Central
   - `AudioSource__Mode`: stays `'Mock'` (env var unchanged)
   - RBAC role assignment: unchanged (uses deterministic guid; auto-reapplies to recreated resource)

### Why This Change

`dataLocation` controls geographic phone number pool availability:
- `'Europe'` → European numbers only (Swedish, German, etc.)
- `'United States'` → US toll-free (1-800, 1-888) and US geographic numbers

**dataLocation is IMMUTABLE** — in-place update fails. Requires delete + recreate.

### Safety & Impact

- No sunk assets: no PSTN phone number purchased, no Event Grid subscription wired
- Ideal time to switch (resource days old)
- RBAC auto-reapplies: deterministic guid() re-computes to new resource ID on next provision
- Zero manual RBAC work needed

### Bicep Validation
- `az bicep build infra/main.bicep` → 0 errors, 0 warnings

### Operator Steps (Not implemented; for next phase)

1. Delete existing ACS resource (`az resource delete ...`)
2. Run `azd provision` (Bicep recreates with dataLocation: 'United States')
3. Verify endpoint updates (same name, same URL pattern)
4. Purchase US toll-free number (portal)
5. Wire Event Grid + Entra delivery auth (next round)
6. Flip `AudioSource__Mode=Acs` (one env var, no rebuild)

**Status:** IMPLEMENTED; awaiting operator delete + `azd provision`
