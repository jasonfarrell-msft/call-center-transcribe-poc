# Architecture & Security Sign-Off: ACS Option C Build Spec
**By:** Athrun — Lead / Architect  
**Date:** 2026-06-08T14:05:26.525-04:00  
**Type:** Architecture Sign-Off + Build Spec  
**Status:** APPROVE TO BUILD  
**Scope:** ACS call-path plumbing (code + Bicep), mock audio default retained

---

## Context

Jason selected **Option C**: build all ACS call-path plumbing (AcsAudioSource + IncomingCall webhook + media-streaming WebSocket route + DI config swap) and supporting Bicep infra (ACS data-plane RBAC, minReplicas), but keep audio mocked: DI default stays MockAudioSource, no PSTN number purchased, Event Grid subscription deferred until routes validated.

This document provides the binding architectural decisions that Dyakka (call-path code) and Meyrin (Bicep) must follow during implementation.

---

## Decision 1: ACS Data-Plane RBAC Role (Least-Privilege)

**Role:** `Communication Services Contributor`  
**GUID:** `2b4609a5-7812-4aba-b5e3-076e6a078419`  
**Scope:** The ACS resource only (not resource group)  
**Assigned to:** ACA Container App system-assigned managed identity (`apiContainerApp.identity.principalId`)

### Justification

There is no narrower built-in Azure role that grants both Call Automation answer/start-media-streaming AND data-plane access without management-plane permissions. The alternatives:
- `Communication Services Reader` — read-only, cannot answer calls.
- `Communication Services User` — client-side token operations only, not server-side Call Automation.

`Communication Services Contributor` is the minimum viable built-in role for Call Automation SDK operations (AnswerCall, StartMediaStreaming). This is an accepted known gap in Azure's RBAC granularity for ACS.

**Residual risk:** This role also allows management-plane operations against the ACS resource (e.g., regenerate keys, update resource properties). Mitigated by: (1) scoping to the single ACS resource, not the resource group; (2) the identity is a system-assigned managed identity with no external exposure; (3) when Microsoft releases a granular `Communication Services Call Automation Client` role, we narrow immediately.

### Bicep Implementation

Extend the existing role assignment module (`modules/acr-pull-role-assignment.bicep`) to support a `communicationServices` scope type, OR create a dedicated call inline. Follow the existing deterministic `guid(resource.id, principalId, roleDefinitionId)` naming pattern.

```bicep
// In main.bicep — add the role definition variable:
var communicationServicesContributorRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '2b4609a5-7812-4aba-b5e3-076e6a078419'
)

// Add a new module invocation (after extending the module to support 'communicationServices' scope):
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

The existing module must be extended: add a `'communicationServices'` option to the `@allowed` decorator, add a `Microsoft.Communication/communicationServices` existing resource reference, and a corresponding conditional role assignment block. Follow the exact pattern of the existing `cognitiveServices` branch.

---

## Decision 2: Webhook Security for IncomingCall Endpoint

**Pick:** SubscriptionValidationEvent handshake + schema validation. Entra-protected delivery auth deferred to the Event Grid wiring round.

### Rationale

This round, no Event Grid subscription exists and no phone number is provisioned. The endpoint is built to validate its contract, not to receive production traffic. Full Entra-protected webhook delivery authentication is the correct long-term solution but has a dependency on the Event Grid subscription (which is explicitly out of scope).

### Implementation Requirements

1. **Handle `SubscriptionValidationEvent`** — the route MUST detect `eventType: "Microsoft.EventGrid.SubscriptionValidationEvent"`, extract `data.validationCode`, and return it in the response body. This is the Event Grid handshake that proves endpoint ownership.

2. **Validate event schema structure** — reject any POST body that doesn't conform to the expected EventGridEvent schema (check `eventType` is one of `Microsoft.EventGrid.SubscriptionValidationEvent` or `Microsoft.Communication.IncomingCall`). Return 400 for anything else. This is defense-in-depth against trivial spoofing.

3. **NO secrets hardcoded. NO HMAC secret in Key Vault for this purpose.** The validation handshake is cryptographic proof of Event Grid ownership. Combined with HTTPS-only delivery, this is sufficient for a POC with no live calls.

4. **Document in code comments:** When Event Grid subscription is wired (next round), Meyrin must add Microsoft Entra delivery authentication (AAD-protected webhook) at that time. This is a blocking prerequisite for going live with a real phone number.

5. **No `[AllowAnonymous]` on the route if `RequireAuth` is enabled globally** — instead, the ACS event routes (`/api/events/acs/*`) must be excluded from the JWT auth policy since Event Grid cannot present a JWT Bearer token (it uses its own delivery auth). Use a separate route group without the `AgentAssistAccess` policy requirement.

---

## Decision 3: Media-Streaming WebSocket Topology

### minReplicas

**Decision:** Change `minReplicas` to a Bicep parameter, **default value = 1**.

Rationale: The POC is short-lived. A forgotten scale-up before demo risks a dropped call (Dyakka's "cardinal sin"). The cost delta (~$15-30/month for an idle 0.5 vCPU container) is negligible for a POC. Make it a param so production patterns can override to 0 later.

```bicep
@description('Minimum replicas for the API Container App. Set to 1 for demo reliability (WebSocket statefulness).')
param apiMinReplicas int = 1
```

### maxReplicas & Affinity

**Decision:** Keep `maxReplicas = 1` (already the current value). Single replica for the POC.

With maxReplicas=1, session affinity is moot — all traffic lands on the same instance. The IncomingCall webhook, the media-streaming WebSocket, and the AcsAudioSource Channel all coexist on the single replica by design. No sticky sessions configuration needed.

### Reconnect / Drop Handling

For the POC:
- If the WebSocket drops mid-call, the `AcsAudioSource` Channel should complete (signal end-of-stream to consumers via channel completion). No automatic reconnect.
- Log a warning-level event on unexpected disconnection.
- The consumer (future audio→Speech service) treats channel completion as end-of-audio-stream.
- **Do NOT** implement reconnect logic this round. A dropped stream in a POC rehearsal = restart the call. Document this as a known limitation.

---

## Decision 4: DI Swap Contract

**Config key:** `AudioSource:Mode`  
**Values:** `"Mock"` (default) | `"Acs"`  
**Default:** `"Mock"` — nothing changes for existing demo/dev workflows.

### Implementation

Replace the current hardcoded registration:

```csharp
// BEFORE (current):
services.AddSingleton<IAudioSource, MockAudioSource>();

// AFTER:
var audioSourceMode = configuration.GetValue<string>("AudioSource:Mode") ?? "Mock";
if (string.Equals(audioSourceMode, "Acs", StringComparison.OrdinalIgnoreCase))
{
    services.AddSingleton<IAudioSource, AcsAudioSource>();
}
else
{
    services.AddSingleton<IAudioSource, MockAudioSource>();
}
```

The `AddCallCenterServices` method needs an `IConfiguration` parameter passed through (or use the builder pattern). Both `MockAudioSource` and `AcsAudioSource` are Singleton lifetime — the Channel inside AcsAudioSource is long-lived per-process.

**No rebuild required to swap** — environment variable `AudioSource__Mode=Acs` on the ACA Container App flips it. This matches the existing env-var injection pattern (`Security__RequireAuth`, `Acs__Endpoint`, etc.).

---

## Decision 5: Scope Guard — IN / OUT

### IN this round (Option C deliverables)

| Item | Owner | Type |
|------|-------|------|
| `AcsAudioSource : IAudioSource` with internal `Channel<AudioFrame>` | Dyakka | Code |
| `POST /api/events/acs/incoming-call` route (validation handshake + IncomingCall handler) | Dyakka | Code |
| `GET /api/calls/media-stream` WebSocket upgrade route | Dyakka | Code |
| DI config swap (`AudioSource:Mode`) | Dyakka | Code |
| NuGet: `Azure.Communication.CallAutomation` added to Telephony project | Dyakka | Code |
| Bicep: ACS RBAC role assignment (Contributor, scoped to ACS resource) | Meyrin | Infra |
| Bicep: `apiMinReplicas` param (default 1) | Meyrin | Infra |
| Bicep: Add `AudioSource__Mode` env var to ACA (value: `Mock`) | Meyrin | Infra |
| Extend role assignment module for `communicationServices` scope type | Meyrin | Infra |

### OUT this round (explicitly deferred)

| Item | Why |
|------|-----|
| PSTN phone number purchase | No number needed while DI defaults to Mock |
| Event Grid system topic + subscription | Deferred until webhook routes are validated (per existing TODO) |
| Entra-protected webhook delivery auth | Blocked on Event Grid subscription (wired together) |
| Audio → Speech background consumer service (`IHostedService`) | Lacus + Meyrin pipeline work; separate deliverable |
| Real audio flowing through the pipeline | DI stays Mock; Acs path is code-complete but not activated |
| Rep join via `AddParticipant` | Depends on phone number + live calls |
| ACS web-client calling SDK (Option B) | Not selected |

### Audio → Speech Consumer Recommendation

**NOT this round. Immediate next round.**

The audio → Speech consumer service (the `IHostedService` that calls `audioSource.ReadAsync()` and feeds bytes to Azure AI Speech SDK) is Lacus + Meyrin's deliverable. It should begin as soon as the `AcsAudioSource` interface implementation lands (it can develop against `MockAudioSource` with a richer mock that yields real PCM frames). But it is NOT a prerequisite for THIS round's deliverables and including it would expand scope beyond the call-path plumbing Jason approved.

**Sequence:** This round lands → Lacus+Meyrin start the consumer service immediately after (can overlap) → Event Grid wiring + Entra auth is the final activation step.

---

## Decision 6: SDK & Auth

**Package:** `Azure.Communication.CallAutomation` — current GA version (add to `CallCenterTranscription.Telephony.csproj`)

**Authentication:** `DefaultAzureCredential` via `Azure.Identity`. The `CallAutomationClient` is constructed with the ACS endpoint (already available as `Acs__Endpoint` env var) and a `DefaultAzureCredential` instance. Zero connection strings. Zero secrets in code.

```csharp
// In AcsAudioSource or a factory:
var client = new CallAutomationClient(
    new Uri(configuration["Acs:Endpoint"]!),
    new DefaultAzureCredential());
```

The system-assigned managed identity on the ACA Container App authenticates automatically via the RBAC role assignment from Decision 1.

---

## VERDICT: ✅ APPROVE TO BUILD

Dyakka and Meyrin may proceed with implementation using the decisions above as binding spec. Key guardrails:

1. **No secrets anywhere.** Managed identity + RBAC is the only auth path.
2. **Mock stays default.** Flipping to Acs requires explicit env var change.
3. **Single replica.** No multi-instance complexity this round.
4. **No Event Grid subscription yet.** Routes are built and validated; wiring happens next round with Entra delivery auth.
5. **No audio consumer service this round.** Frames go into the Channel; nothing reads them until Lacus+Meyrin deliver the next piece.

---

## Sign-Off

| Role | Agent | Verdict |
|------|-------|---------|
| Lead / Architect | Athrun | ✅ APPROVE TO BUILD |
