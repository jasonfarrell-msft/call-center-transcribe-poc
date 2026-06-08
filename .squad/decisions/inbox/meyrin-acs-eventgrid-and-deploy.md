# ACS Event Grid Wiring, Speech RBAC Verification, and API Deploy Recipe

**Date:** 2026-06-08T15:24:21.856-04:00
**By:** Meyrin (Backend Dev)
**Requested by:** Jason
**Status:** READY — pending API image deploy + live subscription create
**Per:** athrun-acs-go-live-signoff.md

---

## 1. Event Grid — Bicep Added (IaC Complete)

### What was added to `infra/main.bicep`

**Variable** (near line ~92):
```bicep
var eventGridSystemTopicName = 'evgt-acs-${uniqueSuffix}'
```
Resolves to `evgt-acs-kdarok` in the live environment.

**System Topic resource** (`Microsoft.EventGrid/systemTopics@2022-06-15`):
- Name: `evgt-acs-${uniqueSuffix}`
- Location: `global` (required for ACS system topics)
- Source: `communicationService.id` (the ACS resource)
- TopicType: `Microsoft.Communication.CommunicationServices`
- Delivery auth: plain webhook per Athrun sign-off (see justification in bicep comment)

**Event Subscription resource** (`Microsoft.EventGrid/systemTopics/eventSubscriptions@2022-06-15`):
- Name: `sub-incoming-call`
- Parent: the system topic above
- Endpoint: `${apiBaseUrl}/api/events/acs/incoming-call` (resolves to ACA FQDN)
- Filter: `Microsoft.Communication.IncomingCall` only
- Schema: `EventGridSchema`
- Retry: 30 attempts, 1440 min TTL (Event Grid defaults — no dead-letter for POC)

**Outputs updated:** `resourceNames.eventGridSystemTopic` added; `serviceEndpoints.acsEventGridWebhookEndpoint` added; `acsLiveAutomationStatus` updated.

**Bicep build:** `az bicep build infra/main.bicep` → **0 errors, 0 warnings**.

### Why Bicep is IaC-complete but NOT the live activation path

A future idempotent `azd provision` will create/upsert both the system topic and the subscription. However, because the subscription creation fires a `SubscriptionValidationEvent` handshake at creation time, a provision run against an ACA with only the placeholder image (no webhook handler) will fail the subscription creation. **The safe live activation path is surgical `az` commands, sequenced after API deploy** (see Section 4).

---

## 2. Speech RBAC — Verified LIVE (No Action Required)

**Verification command run:**
```bash
az role assignment list \
  --scope /subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.CognitiveServices/accounts/speech-cctrans-kdarok \
  --query "[?principalId=='6edcf409-903a-49ec-ae48-aed391da1fa7'].{roleDefinitionName:roleDefinitionName,roleDefinitionId:roleDefinitionId}"
```

**Result: PRESENT**
```json
[
  {
    "roleDefinitionName": "Cognitive Services User",
    "roleDefinitionId": "/subscriptions/bb4b2781.../providers/Microsoft.Authorization/roleDefinitions/a97b65f3-24c7-4388-baec-2e87135dc908",
    "principalId": "6edcf409-903a-49ec-ae48-aed391da1fa7"
  }
]
```

**Role GUID verification:** `az role definition list --name "Cognitive Services User"` confirmed GUID `a97b65f3-24c7-4388-baec-2e87135dc908` exists in this directory — no apply needed.

**Status: No surgical fix required.** The ACA system-assigned MI (`6edcf409-903a-49ec-ae48-aed391da1fa7`) already has `Cognitive Services User` on `speech-cctrans-kdarok`. The consumer (Lacus's `SpeechTranscriptionService`) will auth successfully via `DefaultAzureCredential` when deployed.

---

## 3. API (ACA) Deploy — Investigation + Recipe

### What azure.yaml shows

```yaml
services:
  api:
    project: src/CallCenterTranscription.Api
    language: dotnet
    host: containerapp
    docker:
      path: ./Dockerfile
      context: ../..
      remoteBuild: true
```

The `api` service is a Container App with `remoteBuild: true` — meaning `azd deploy api` sends source to ACR Tasks for the build rather than building locally. The Dockerfile is at `src/CallCenterTranscription.Api/Dockerfile`.

### Workflow gap

There is **no `.github/workflows/` file for the API**. Only `deploy-frontend.yml` (Web/App Service) and squad automation workflows exist. The API has no CI/CD pipeline. Deploy relies on `azd deploy api` or a manual `az` path.

### azd env problem

The `.azure/` environment is bare — only `AZURE_ENV_NAME` is set. A full `azd deploy api` would require reconstructing the env (subscription, resource group, location, all output values). This is risky and not the recommended path per Athrun.

### Safest deploy path: `az acr build` + `az containerapp update`

This uses the existing ACR (`acrcctranskdarok.azurecr.io`) as both the build service and image registry, which is already wired to the ACA via the UAMI ACR-pull role.

---

## 4. Ordered Go-Live Live Command Sequence

### Prerequisites (Lacus must complete first)
- `SpeechTranscriptionService` merged + image-ready
- `DemoSafety:DataMode` guard removed from `Program.cs`

### Step 1 — Build and push API image to ACR (ACR Tasks remote build)

```bash
# Run from repo root
az acr build \
  --registry acrcctranskdarok \
  --image api:live-$(date +%Y%m%d%H%M) \
  --file src/CallCenterTranscription.Api/Dockerfile \
  .
```

This streams the build context to ACR Tasks (no local Docker required). The image tag `live-YYYYMMDDHHMM` is human-readable and timestamped. Note the actual tag printed by the command — you'll need it in Step 2.

### Step 2 — Update ACA to use the new image

```bash
az containerapp update \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --image acrcctranskdarok.azurecr.io/api:<TAG-FROM-STEP-1>
```

Wait for the revision to become active:
```bash
az containerapp revision list \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --query "[].{name:name, active:properties.active, createdTime:properties.createdTime}" \
  -o table
```

### Step 3 — Verify webhook handler is reachable

```bash
curl -I https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/healthz
```

Expect `200 OK`. The ACA currently has `minReplicas=0` — the replica must be warm before the next step. The Event Grid subscription creation itself will trigger a cold start if the replica is sleeping, but that race condition can cause the handshake to time out. **Confirm the replica is running before Step 4.**

### Step 4 — Create Event Grid System Topic (safe — no handshake)

```bash
az eventgrid system-topic create \
  --name evgt-acs-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --source /subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.Communication/communicationServices/acs-cctrans-kdarok \
  --topic-type Microsoft.Communication.CommunicationServices \
  --location global
```

This creates only the topic — no handshake, safe to run anytime.

### Step 5 — Create Event Grid Event Subscription (triggers SubscriptionValidationEvent)

⚠️ **Run only after Step 3 confirms webhook is reachable.** Event Grid will POST a `SubscriptionValidationEvent` to the endpoint and require a valid `validationResponse` in the reply within ~30 seconds.

```bash
az eventgrid system-topic event-subscription create \
  --name sub-incoming-call \
  --system-topic-name evgt-acs-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --endpoint https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/api/events/acs/incoming-call \
  --endpoint-type webhook \
  --included-event-types Microsoft.Communication.IncomingCall \
  --max-delivery-attempts 30 \
  --event-ttl 1440
```

Verify subscription provisioning state:
```bash
az eventgrid system-topic event-subscription show \
  --name sub-incoming-call \
  --system-topic-name evgt-acs-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --query "{provisioningState:provisioningState, endpoint:destination.endpointBaseUrl}" \
  -o json
```

Expect `"provisioningState": "Succeeded"`.

### Step 6 — Flip minReplicas + AudioSource__Mode (Coordinator step — NOT Meyrin)

Per Athrun go-live sequencing, this is the final flip after consumer + Event Grid are validated:
```bash
az containerapp update \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --min-replicas 1 \
  --set-env-vars AudioSource__Mode=Acs
```

---

## 5. Bicep Consistency Confirmation (Task 4)

Both required fields are present and correct in `infra/main.bicep`:

| Field | Location | Value | Status |
|-------|----------|-------|--------|
| `apiMinReplicas` param | Line ~33 | `int = 1` with description | ✅ Correct |
| `scale.minReplicas` wire | ACA template `scale` block | `minReplicas: apiMinReplicas` | ✅ Correct |
| `AudioSource__Mode` env var | ACA container `env` array | `value: 'Mock'` with flip-to-Acs comment | ✅ Correct |
| `main.parameters.json` | `apiMinReplicas.value` | `1` | ✅ Correct |

A future idempotent `azd provision` will reflect the intended end-state. The live flip to `minReplicas=1` and `AudioSource__Mode=Acs` is a surgical `az containerapp update` (Step 6 above), done by the Coordinator only after consumer + Event Grid are validated.

---

## 6. What Stays Deferred

- **Live subscription create** — blocked on Lacus's consumer PR merging and API image deploy (Steps 1–3)
- **minReplicas=1 + Mode=Acs flip** — Coordinator final step (Step 6), after Dyakka validates end-to-end
- **Entra delivery auth on Event Grid** — explicitly deferred for POC per Athrun; required before any production promotion
- **API CI/CD workflow** — no pipeline for the api service; manual `az acr build` + `az containerapp update` is the deploy path until a workflow is added

---

## 7. Resource Reference Quick-Sheet

| Resource | Name | ID |
|----------|------|----|
| Resource Group | rg-callcentertranscribe-swc-mx01 | — |
| ACS | acs-cctrans-kdarok | `/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.Communication/communicationServices/acs-cctrans-kdarok` |
| Speech | speech-cctrans-kdarok | `/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.CognitiveServices/accounts/speech-cctrans-kdarok` |
| ACR | acrcctranskdarok | `acrcctranskdarok.azurecr.io` |
| ACA | ca-api-cctrans-kdarok | FQDN: `ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io` |
| ACA System MI | — | principalId: `6edcf409-903a-49ec-ae48-aed391da1fa7` |
| EG System Topic (to create) | evgt-acs-kdarok | (created in Step 4) |
| Subscription | bb4b2781-6739-4fa1-994e-4ad6ce55c59c | — |
