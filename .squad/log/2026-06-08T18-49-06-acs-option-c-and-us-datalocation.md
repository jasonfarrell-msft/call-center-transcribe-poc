# Session Log: ACS Option C Plumbing + US dataLocation Flip

**Session ID:** acs-option-c-and-us-datalocation  
**Date:** 2026-06-08T14:00:00Z – 2026-06-08T18:50:00Z (UTC)  
**Team:** Dyakka (ACS specialist), Athrun (Architect), Meyrin (Backend/Infra)  
**Status:** ✅ COMPLETE

---

## What Was Delivered

### 1. ACS Assessment & Planning (Dyakka)
- Comprehensive gap analysis: ACS resource exists (Europe dataLocation), but call-path plumbing missing
- Identified 6 prerequisites (RBAC, minReplicas, phone number, Event Grid, routes, AcsAudioSource)
- Presented 3 options; recommended Option C (plumbing + mock default) as safe, unblocking path
- Requested Jason's steering on 6 decisions (Swedish number?, real inbound vs ACS web?, live level?, minReplicas permanent?, webhook security?, RBAC role?)

### 2. ACS Option C Plumbing Built (Dyakka)
**Build result:** 0 errors, 0 warnings, 25/26 tests pass (1 pre-existing UI failure)

**Code delivered:**
- `AcsAudioSource : IAudioSource` — Channel-backed, WebSocket deserialize, PCM 16-bit mono 16kHz format
- Routes: `POST /api/events/acs/incoming-call`, `POST /api/events/acs/callbacks`, WebSocket `/api/calls/media-stream`
- DI swap: `AudioSource:Mode` config (default `"Mock"`, flip to `"Acs"` for live path)
- NuGet: `Azure.Communication.CallAutomation` 1.5.1 GA, `Azure.Identity` 1.21.0

**Dormant until live flip:**
- All three routes (no Event Grid, no phone number)
- AcsAudioSource Channel (empty in Mock mode)
- CallAutomationClient (registered but never called)

### 3. ACS Option C Architecture & Security Sign-Off (Athrun)
**Verdict:** ✅ APPROVE TO BUILD (binding spec for Dyakka + Meyrin)

**Decisions:**
- RBAC: `Communication Services Contributor` scoped to ACS resource on ACA system identity
- Webhook: SubscriptionValidationEvent handshake + schema validation (Entra auth deferred to Event Grid wiring)
- minReplicas: 1 (prevents cold-start call drops; ~$15–30/month negligible cost for POC)
- DI swap: `AudioSource:Mode` config, default `"Mock"`
- Auth: `DefaultAzureCredential` (managed identity), zero secrets in code

### 4. Infra — Option C Bicep (Meyrin)
**Build result:** 0 errors, 0 warnings

**Implemented:**
- Extended role assignment module to support `'communicationServices'` scope type
- ACS RBAC: `Communication Services Contributor` deterministically named via guid()
- minReplicas: 1 (param, default 1)
- AudioSource__Mode: `'Mock'` env var on ACA (activation path: flip to `'Acs'` next round)

### 5. US Phone Number Feasibility (Dyakka)
Jason asked: "Can we use US numbers? Deploy to East US or East US 2?"

**Key correction:** ACS has no per-region deployment. `dataLocation` (not compute region) controls number availability.
- Current: `'Europe'` → European numbers only
- Needed: `'United States'` → US toll-free + geographic

**Critical detail:** `dataLocation` is immutable; in-place update fails. Requires delete + recreate.

**Recommendation:** US toll-free (fastest, no US address, no regulatory wait).

**Path:** Verify subscription eligibility → flip dataLocation → delete existing ACS → `azd provision` → purchase number → wire Event Grid → flip AudioSource__Mode

### 6. Infra — dataLocation Flip (Meyrin)
**Status:** IMPLEMENTED

**Changes:**
- `infra/main.bicep` param default: `'Europe'` → `'United States'`
- `infra/main.parameters.json` authoritative value: `"Europe"` → `"United States"`
- Comment block added explaining immutability + recreate requirement
- AudioSource__Mode: unchanged (`'Mock'`)
- RBAC: auto-reapplies via deterministic guid() to new resource ID

**Safety:** Zero sunk assets (no phone number, no Event Grid). Ideal time to switch.

### 7. dataLocation Flip Reviewer Gate (Athrun)
**Verdict:** ✅ APPROVE (high confidence)

**Verified:**
- Effective value reaching ACS: `'United States'` with correct casing
- RBAC determinism on recreate: ✅ (guid() scoped to new resource ID)
- AudioSource__Mode: ✅ still `'Mock'`
- Scope: precisely limited (no drift, no scope creep)
- Secrets: zero introduced
- Bicep: 0 errors, 0 warnings
- Operator documentation: explicit (delete step captured; no risk of missed precondition)

---

## OPERATOR NEXT STEPS (BLOCKING)

### Phase A: Recreate ACS with US dataLocation
1. **Delete existing ACS resource**
   ```bash
   az resource delete \
     --resource-type Microsoft.Communication/communicationServices \
     --name <acs-resource-name> \
     --resource-group <rg-name>
   ```
   Get exact resource name from `azd env get-values` or portal.

2. **Run `azd provision`**
   - Bicep recreates ACS with `dataLocation: 'United States'`
   - RBAC role assignment auto-applies to new resource
   - `Acs__Endpoint` env var auto-updates (same naming scheme, same resource name)

3. **Verify in portal**
   - ACS resource → check `dataLocation` reads `'United States'` (no stale `'Europe'`)
   - Confirm `Acs__Endpoint` matches new resource endpoint

### Phase B: Acquire US Toll-Free Number (Portal)
1. Navigate to ACS resource → **Phone Numbers** → **Get Phone Number**
2. Search for **US toll-free** (1-800, 1-888, etc.)
3. **PREREQUISITE GATE:** If subscription is ineligible (trial, free-tier, etc.), portal will surface error. Resolve subscription eligibility first.
4. Provision number (takes minutes; no US address required)
5. Note the number for demo

### Phase C: Next Round — Event Grid + Entra Auth + Audio→Speech Consumer
**Not this round; sequential next round:**
1. Wire Event Grid system topic + subscription (`Microsoft.Communication.IncomingCall` → `POST /api/events/acs/incoming-call`)
2. Entra-protected webhook delivery auth (blocking prerequisite for live)
3. Build audio→Speech consumer (`IHostedService` reading `IAudioSource.ReadAsync()` → Azure AI Speech SDK)
4. Coordinate Lacus + Meyrin for consumer service
5. Validate webhook + audio pipeline against mock audio first

### Phase D: Go Live (One Env Var Flip)
1. Flip `AudioSource__Mode=Acs` on ACA env vars
2. Redeploy ACA (or set env var directly if runtime reload is configured)
3. **Demo is live:** inbound calls → ACS → media streaming → `AcsAudioSource` → Speech SDK → transcript → dashboard

---

## Key Decisions Captured

| Decision | Owner | Status |
|----------|-------|--------|
| Option C (plumbing + mock default) | Jason | ✅ Selected |
| ACS RBAC role (`Communication Services Contributor`) | Athrun | ✅ Signed off |
| minReplicas = 1 (permanent Bicep param) | Athrun | ✅ Signed off |
| dataLocation → `'United States'` | Jason | ✅ Approved (Meyrin implemented) |
| DI swap config (`AudioSource:Mode`) | Athrun | ✅ Signed off |
| Webhook security (SubscriptionValidationEvent + schema) | Athrun | ✅ Signed off |
| US toll-free recommended | Dyakka | ✅ Advisory |

---

## Build Verification

| Component | Result |
|-----------|--------|
| `dotnet build CallCenterTranscription.sln -c Release` | 0 errors, 0 warnings |
| Tests (26 total) | 25 pass, 1 pre-existing UI failure |
| Bicep `main.bicep` | 0 errors, 0 warnings |
| Bicep `acr-pull-role-assignment.bicep` (extended) | 0 errors, 0 warnings |

---

## Known Limitations (POC Stage)

1. **WebSocket reconnect:** No logic this round. Drop mid-call = restart call. Documented limitation.
2. **minReplicas cold-start:** Now 1 (warm always), but cost-conscious prod patterns can override to 0 (must provision phone number + Event Grid subscription separately to avoid demo drop).
3. **Audio→Speech consumer:** Not implemented this round. AcsAudioSource.ReadAsync() delivers frames; consumers will read them when the service is built (Lacus + Meyrin next).
4. **Multi-replica affinity:** Not needed (maxReplicas = 1). No sticky sessions configuration.

---

## Commits

**Session coordinator committed:**
- Dyakka's Option C plumbing code (AcsAudioSource, routes, DI swap, NuGet)
- Meyrin's Bicep (RBAC extended module, minReplicas param, AudioSource__Mode env var)
- Meyrin's dataLocation flip (main.bicep + main.parameters.json)
- All infra + app code changes
- `.gitignore` updated to exclude `infra/main.json` (Bicep build artifact)

---

## Team Sign-Offs

| Role | Agent | Status |
|------|-------|--------|
| ACS/Telephony Specialist | Dyakka | ✅ Assessment + Plumbing delivered; US feasibility advisory |
| Lead / Architect | Athrun | ✅ Option C signed off; dataLocation flip reviewed and approved |
| Backend / Infra Dev | Meyrin | ✅ Option C Bicep delivered; dataLocation flip implemented |

---

## Session Complete

All Option C deliverables code-complete and committed. All Bicep validated. dataLocation flipped to US for toll-free acquisition. Operator ready to delete existing ACS + reprovision. Next session: Event Grid wiring + Entra auth + audio→Speech consumer build-out.
