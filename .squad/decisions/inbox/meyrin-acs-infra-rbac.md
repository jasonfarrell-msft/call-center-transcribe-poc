# Infra Decision: ACS Option C Bicep — RBAC, minReplicas, AudioSource__Mode

**By:** Meyrin — Backend Dev  
**Date:** 2026-06-08T14:05:26.535-04:00  
**Type:** Infrastructure Decision Record  
**Status:** IMPLEMENTED  
**Implements:** Athrun's ACS Option C sign-off (`athrun-acs-option-c-signoff.md`)  
**Files changed:** `infra/main.bicep`, `infra/modules/acr-pull-role-assignment.bicep`, `infra/main.parameters.json`

---

## Decision 1: ACS Data-Plane RBAC Role Assignment

**Role:** `Communication Services Contributor`  
**Role Definition ID:** `2b4609a5-7812-4aba-b5e3-076e6a078419`  
**Scope:** The single `Microsoft.Communication/communicationServices` resource (not resource group, not subscription)  
**Principal:** `apiContainerApp.identity.principalId` — ACA Container App **system-assigned** managed identity  

### Implementation

Extended `modules/acr-pull-role-assignment.bicep` to support a `'communicationServices'` scopeType alongside the existing `'acr'`, `'keyVault'`, and `'cognitiveServices'` branches:

- Added `'communicationServices'` to the `@allowed` decorator.
- Added `resource communicationServicesAccount 'Microsoft.Communication/communicationServices@2025-05-01' existing = if (scopeType == 'communicationServices')`.
- Added `resource communicationServicesScopedRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (scopeType == 'communicationServices')` with:
  - `name: guid(communicationServicesAccount.id, principalId, roleDefinitionId)` — deterministic, idempotent, matches the existing cognitiveServices pattern exactly.
  - `scope: communicationServicesAccount`
  - `principalType: 'ServicePrincipal'`

Called from `main.bicep` as:
```bicep
module apiToAcsRoleAssignment 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiToAcsRoleAssignment'
  params: {
    scopeType: 'communicationServices'
    scopeName: communicationService.name
    principalId: apiContainerApp.identity.principalId
    roleDefinitionId: communicationServicesContributorRoleDefinitionId
  }
}
```

### Justification (per Athrun's sign-off)

No narrower Azure built-in role covers both Call Automation `AnswerCall` and `StartMediaStreaming`. The alternatives fall short:
- `Communication Services Reader` — read-only, cannot answer calls.
- `Communication Services User` — client-side token operations only, not server-side Call Automation.

**Residual risk mitigation:**
1. Scoped to the single ACS resource — not the resource group or subscription.
2. Assigned to the system-assigned managed identity — no external exposure, no credential leakage.
3. When Microsoft ships a dedicated `Communication Services Call Automation Client` role, narrow immediately.

This is an accepted known gap in Azure RBAC granularity for ACS at POC stage.

---

## Decision 2: minReplicas = 1 (Parameter)

**Before:** `minReplicas: 0` (hardcoded in scale block)  
**After:** `minReplicas: apiMinReplicas` with `param apiMinReplicas int = 1`

**Rationale:** A cold replica (0) would drop an inbound call during the demo — the ACA Container App must be warm when ACS delivers the incoming-call event. Default 1 ensures continuous availability at negligible cost (~$15–30/month for 0.5 vCPU idle). Making it a param allows production patterns to override to 0 later without a code change.

`maxReplicas` confirmed at 1 — unchanged. Single replica means session affinity is moot (all traffic lands on the same instance by design).

`main.parameters.json` updated with `"apiMinReplicas": { "value": 1 }`.

---

## Decision 3: AudioSource__Mode = Mock (Env Var)

**Env var added:** `AudioSource__Mode = 'Mock'` on the ACA Container App.

**Rationale:** Dyakka's DI registration reads `AudioSource:Mode` from `IConfiguration`. The double-underscore format maps to the colon-separated key under ASP.NET Core's environment variable configuration provider. Setting it to `'Mock'` preserves the existing default (`MockAudioSource`) — no behaviour change today.

**Activation path (next round):** Once the ACS phone number is provisioned and the Event Grid subscription is wired, flip this env var to `'Acs'` via ACA env var update. No image rebuild required.

---

## Deferred (Out of scope this round — per Athrun's sign-off)

| Item | Reason |
|------|--------|
| PSTN phone number | No number needed while DI defaults to Mock |
| Event Grid system topic + subscription | Deferred until webhook routes are validated |
| Entra-protected webhook delivery auth | Blocked on Event Grid subscription wiring |
| Audio → Speech background consumer (`IHostedService`) | Lacus + Meyrin next deliverable |
| `AudioSource__Mode` flip to `Acs` | Blocked on phone number + Event Grid |
| ACS connection string / any new secret | Out of scope — zero secrets policy enforced |

---

## Bicep Validation

`az bicep build infra/main.bicep` — **0 errors, 0 warnings**.
