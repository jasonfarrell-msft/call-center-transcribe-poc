# Dyakka Run 3: US Phone Number Feasibility Advisory

**Date:** 2026-06-08T14:05:26Z  
**Agent:** Dyakka (ACS/Telephony Specialist)  
**Phase:** Advisory — feasibility analysis  
**Deliverable:** `dyakka-us-numbers-feasibility.md`

## Summary

Jason asked: "Can we use American-based numbers? Deploy to East US or East US 2 if needed?"

**Answer:** Yes — but with one critical correction.

### Key Distinction

ACS `location` is always `'global'` — there is no per-region deployment slot. **`dataLocation` is what controls number availability**, not compute region.

| Concept | Current | Required for US |
|---|---|---|
| `dataLocation` (ACS data residency) | `'Europe'` | `'United States'` |
| `location` (ARM) | `'global'` | `'global'` (unchanged) |
| ACA compute region | Sweden Central | Can stay Sweden Central |

### US Number Types

| Type | Call Automation | US Address | Approval Wait | Demo Suitability |
|---|---|---|---|---|
| **Toll-free** (1-800, 1-888) | ✅ | ❌ | None | ✅ **Recommended** |
| **Geographic** (area code) | ✅ | ✅ | Days–weeks | ⚠️ Slower |

**Recommendation: Toll-free** — no US address, no regulatory wait, near-instant provision.

### Critical Path

1. **Verify subscription eligibility** — Portal: ACS resource → Phone Numbers → Get Phone Number → search US toll-free. If blocked, subscription type is the gating issue.
2. **`dataLocation` is IMMUTABLE** — in-place update will fail. Must delete and recreate ACS resource.
3. **Bicep change** (1 line): `communicationDataLocation: 'Europe'` → `'United States'`
4. **Delete existing ACS resource** (zero sunk assets — no phone number purchased, no Event Grid wired)
5. **`azd provision`** — new ACS with US residency; RBAC auto-reapplies
6. **Portal: acquire US toll-free**
7. **Wire Event Grid + Entra delivery auth** (next round)
8. **Flip `AudioSource__Mode=Acs`** (one env var, no rebuild)

**Status:** Feasibility confirmed; Jason's decision required on subscription eligibility and US dataLocation flip
