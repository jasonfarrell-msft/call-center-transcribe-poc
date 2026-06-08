# Session Log — ACS Go-Live Build (2026-06-08T19:24:21Z)

**Session:** ACS go-live build after US toll-free number +18774178275 purchased  
**Coordinator captured:** +18774178275 as directive (copilot-directive inbox file)  
**Live state verified:** ACA ca-api-cctrans-kdarok minReplicas=0, AudioSource__Mode unset, no Event Grid, no audio→Speech consumer  

---

## Agent Decisions & Deliverables

| Agent | Decision | Commit | Status |
|-------|----------|--------|--------|
| **Athrun** | Go-Live Architecture Sign-Off | — | APPROVE TO BUILD |
| **Lacus** | SpeechTranscriptionService (consumer) | 7426ebe | IMPLEMENTED |
| **Meyrin** | Event Grid Bicep + RBAC verification | 9f28cdd | READY |
| **Meyrin** | Speech env vars fix | 4decb78 | COMPLETE |
| **Athrun** | Go-Live Build Review | — | REQUEST CHANGES (FIXED) |

---

## PENDING: Live Go-Live Sequence (6 Atomic Steps)

**⚠️ DO NOT execute until all pre-conditions verified (consumer built, Athrun gate passed, RBAC live)**

### Prerequisites (completed)
- ✅ SpeechTranscriptionService built + tested (commit 7426ebe)
- ✅ DemoSafety:DataMode guard removed from Program.cs
- ✅ Speech RBAC verified live (a97b65f3 on speech-cctrans-kdarok, principal 6edcf409)
- ✅ Event Grid Bicep complete (commit 9f28cdd + 4decb78)
- ✅ Athrun gate: REQUEST CHANGES → FIXED (Speech__Region + Speech__ResourceId added)

### Step 1: Build and Push API Image to ACR

```bash
az acr build \
  --registry acrcctranskdarok \
  --image api:live-$(date +%Y%m%d%H%M) \
  --file src/CallCenterTranscription.Api/Dockerfile \
  .
```

**Output:** Image tag printed (e.g., `api:live-202606081524`). **Note this tag for Step 2.**

### Step 2: Update ACA to Use New Image

```bash
az containerapp update \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --image acrcctranskdarok.azurecr.io/api:<TAG-FROM-STEP-1>
```

**Wait for revision to become active:**
```bash
az containerapp revision list \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --query "[].{name:name, active:properties.active, createdTime:properties.createdTime}" \
  -o table
```

### Step 3: Verify Webhook Handler Is Reachable

```bash
curl -I https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/healthz
```

**Expected:** `200 OK`  
**⚠️ CRITICAL:** Replica must be warm (not sleeping at minReplicas=0) before Event Grid subscription creation. If 503, wait 15–30 seconds and retry.

### Step 4: Create Event Grid System Topic

```bash
az eventgrid system-topic create \
  --name evgt-acs-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --source /subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.Communication/communicationServices/acs-cctrans-kdarok \
  --topic-type Microsoft.Communication.CommunicationServices \
  --location global
```

**Status:** No handshake; safe to run anytime after Step 1.

### Step 5: Create Event Grid Event Subscription (Triggers Handshake)

⚠️ **Run only after Step 3 confirms webhook is reachable (200 OK).**

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

**Verify subscription is provisioned:**
```bash
az eventgrid system-topic event-subscription show \
  --name sub-incoming-call \
  --system-topic-name evgt-acs-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --query "{provisioningState:provisioningState, endpoint:destination.endpointBaseUrl}" \
  -o json
```

**Expected:** `"provisioningState": "Succeeded"`

### Step 6: Flip minReplicas + AudioSource__Mode + Speech Env Vars (FINAL — ATOMIC)

⚠️ **After this step, live calls will trigger the Speech consumer. No rollback without Mode=Mock flip.**

```bash
az containerapp update \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --min-replicas 1 \
  --set-env-vars \
    AudioSource__Mode=Acs \
    Speech__Region=swedencentral \
    Speech__ResourceId=/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.CognitiveServices/accounts/speech-cctrans-kdarok
```

**Verify replica is warm:**
```bash
az containerapp revision list \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --query "[0].properties.replicas" \
  -o json
```

**Expected:** `1` (minReplicas=1 is now live).

---

## End-to-End Test (Dyakka — Post-Step 6)

1. **Inbound call test:** Call +18774178275 from any phone.
2. **Expected flow:**
   - ACS receives call → Event Grid IncomingCall event POSTs to webhook
   - `/api/events/acs/incoming-call` handler answers call via `AnswerCallAsync`
   - Audio streams into ACS Channel (WebSocket)
   - Consumer reads frames from Channel → pushes to Speech SDK
   - Speech SDK emits Recognizing (interim) + Recognized (final) events
   - Consumer pushes TranscriptEvent (isFinal=false/true) to SignalR group call:{callId}
   - Rep UI receives live transcript stream (no refresh required)

3. **Verification checklist:**
   - [ ] Call connects (caller hears ringback / ACS prompt)
   - [ ] Caller hears background audio (rep or system audio mixed)
   - [ ] Rep UI shows live transcript stream (not blank/cached)
   - [ ] Interim results appear first, then finalized text
   - [ ] Call disconnects cleanly after ~30–60 seconds (demo test duration)

---

## Demo-Honesty Fallback (If Live Path Fails)

If anything in Steps 1–6 fails or the live call shows no transcript, flip back to Mock mode immediately:

```bash
az containerapp update \
  --name ca-api-cctrans-kdarok \
  --resource-group rg-callcentertranscribe-swc-mx01 \
  --set-env-vars AudioSource__Mode=Mock
```

**Effect:** Consumer switches to MockAudioSource (no real audio frames) → scripted propane feed REST endpoints remain live → demo can continue using scripted transcript data.

**Recovery time:** ~10–15 seconds (new revision deployment + warm replica).

---

## Critical Resources & IDs

| Resource | ID / FQDN |
|----------|-----------|
| Subscription | bb4b2781-6739-4fa1-994e-4ad6ce55c59c |
| Resource Group | rg-callcentertranscribe-swc-mx01 |
| ACS | acs-cctrans-kdarok |
| Speech | speech-cctrans-kdarok |
| ACR | acrcctranskdarok.azurecr.io |
| ACA API | ca-api-cctrans-kdarok |
| ACA FQDN | ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io |
| ACA System MI | 6edcf409-903a-49ec-ae48-aed391da1fa7 |
| EG System Topic | evgt-acs-kdarok |
| EG Subscription | sub-incoming-call |
| Phone Number | +18774178275 |

---

## Owner Assignment

- **Step 1–6:** Coordinator (Jason / Scribe executing on behalf)
- **Post-Step 6 test + runbook:** Dyakka (ACS dual-call / demo runbook owner)
- **Gate review + decision:** Athrun (Lead/Architect)
