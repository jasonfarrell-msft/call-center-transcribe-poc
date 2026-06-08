# ACS Go-Live Architecture Sign-Off

**Date:** 2026-06-08T15:24:21-04:00  
**By:** Athrun (Lead/Architect)  
**Requested by:** Jason  
**Status:** APPROVE TO BUILD  
**Scope:** Inbound call → live transcript on rep dashboard

---

## Decision 1: Event Grid IncomingCall Wiring + Delivery Auth

### Pick: Plain webhook (SubscriptionValidationEvent handshake only)

**Justification for POC:**

The endpoint is `AllowAnonymous`. A forged `IncomingCall` POST to this endpoint would trigger `AnswerCallAsync` with a bogus `incomingCallContext` — ACS would reject it with a 4xx/failure because the context string isn't a valid pending call. The worst case is a logged error and a wasted HTTP call to ACS. There is no data exfiltration, no state corruption, no cost impact (AnswerCall on an invalid context doesn't start billing). The endpoint cannot be weaponized for DDoS because it's a single lightweight HTTP POST that fails fast.

**Entra-protected delivery auth is DEFERRED** — the incremental security gain doesn't justify the setup complexity for a non-production POC with a toll-free number that will be active only during demo windows.

**Residual risk accepted:** An attacker who discovers the FQDN + path could spam the endpoint with junk payloads. Mitigation: ACA has built-in rate limiting on public ingress; we add structured logging so abuse is visible. If this moves toward production, Entra delivery auth becomes a hard requirement.

### What Meyrin must provision (surgical `az` commands):

1. **Event Grid System Topic** on the ACS resource:
   ```
   az eventgrid system-topic create \
     --name evgt-acs-cctrans \
     --resource-group rg-callcentertranscribe-swc-mx01 \
     --source /subscriptions/{sub}/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.Communication/communicationServices/acs-cctrans-kdarok \
     --topic-type Microsoft.Communication.CommunicationServices \
     --location global
   ```

2. **Event Subscription** filtered to IncomingCall only:
   ```
   az eventgrid system-topic event-subscription create \
     --name sub-incoming-call \
     --system-topic-name evgt-acs-cctrans \
     --resource-group rg-callcentertranscribe-swc-mx01 \
     --endpoint https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/api/events/acs/incoming-call \
     --endpoint-type webhook \
     --included-event-types Microsoft.Communication.IncomingCall
   ```

3. **Retry/dead-letter:** Use Event Grid defaults (30 attempts, exponential backoff, 24h TTL). No dead-letter storage for POC — missed calls are observable in ACA logs. A failed delivery simply means the call wasn't answered (caller hears ring then hang-up).

4. **Add to Bicep (non-blocking):** Add the system topic + subscription to `main.bicep` under the existing `TODO(event-grid)` comment for IaC consistency. But the LIVE activation is via surgical `az` — do not depend on `azd provision`.

---

## Decision 2: minReplicas + AudioSource Mode — Mechanism & Sequencing

### Mechanism: Surgical `az containerapp update`

Given the bare `azd` environment (only `AZURE_ENV_NAME` set), a full `azd provision` is risky — it could recreate resources with missing parameter values or drift from the actual live state. Surgical `az` commands are the lower-risk path consistent with how the ACS resource was already recreated.

### Sequencing (critical — do NOT flip all at once):

| Step | Action | Rationale |
|------|--------|-----------|
| 1 | Build + deploy the Speech consumer (Decision 3) | Without a consumer, Mode=Acs means audio flows into a Channel nothing reads — no transcript |
| 2 | Verify Speech RBAC is live (Decision 4) | Consumer will fail auth without this |
| 3 | Create Event Grid system topic + subscription | The call-trigger mechanism |
| 4 | `az containerapp update --name ca-api-cctrans-kdarok --resource-group rg-callcentertranscribe-swc-mx01 --min-replicas 1 --set-env-vars AudioSource__Mode=Acs` | Atomic flip: warm replica + live audio source |
| 5 | Test call to +18774178275 → verify transcript in UI | End-to-end validation |

### Intended end-state:
- `minReplicas = 1` (warm; matches Bicep param `apiMinReplicas` default)
- `AudioSource__Mode = Acs` (live ACS audio path)
- `maxReplicas = 1` (unchanged)

### Confirm: Do NOT flip Mode=Acs until the consumer + Event Grid are ready.
Flipping Mode=Acs without the consumer means calls connect, audio streams into the Channel, but produces zero transcript — a silent failure that looks broken to a demo observer. Keep Mock until Step 4.

---

## Decision 3: Audio→Speech Consumer — Shape + Dependencies

### Required: Yes. Without this, a live call produces no visible output.

### Shape: `SpeechTranscriptionService : BackgroundService`

**Location:** `src/CallCenterTranscription.Api/Services/SpeechTranscriptionService.cs`  
(Lives in Api project because it needs `IHubContext<PipelineHub>` — keep the DI graph simple for POC.)

**Behavior:**

```
ExecuteAsync(CancellationToken):
  1. Resolve IAudioSource (will be AcsAudioSource when Mode=Acs)
  2. Create SpeechRecognizer (continuous recognition, push stream)
  3. await foreach (frame in audioSource.ReadAsync(ct)):
       pushStream.Write(frame.Payload)
  4. On Recognizing → emit interim TranscriptEvent (isFinal=false) via SignalR
  5. On Recognized → emit final TranscriptEvent (isFinal=true) via SignalR
  6. On stream end → stop recognizer, log completion
```

**SDK / Package:** `Microsoft.CognitiveServices.Speech` (latest GA NuGet — currently 1.42.x). This is the Speech SDK, NOT the REST API — continuous recognition with push audio stream requires the native SDK.

**Azure Resource:** `speech-cctrans-kdarok` (already provisioned in Bicep as `speechAccount`, kind `SpeechServices`, region `swedencentral`). Endpoint: `https://speechcctrans{suffix}.cognitiveservices.azure.com/`

**Auth:** `DefaultAzureCredential` → the Speech SDK supports token-based auth via `Azure.Identity`. Construct a `SpeechConfig` with the Speech resource's endpoint and a token from DefaultAzureCredential (scope: `https://cognitiveservices.azure.com/.default`).

**Audio format config:**
- PCM 16-bit mono 16,000 Hz (matches AudioFrame contract exactly)
- Use `AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1)` + `PushAudioInputStream`

**Recognition config:**
- Continuous recognition (`StartContinuousRecognitionAsync`)
- Language auto-detect with candidate languages from `Speech:CandidateLanguages` config (already in env: `en-US,sv-SE,de-DE,fr-FR`)
- **Interim results:** Emit `Recognizing` events as `isFinal=false` TranscriptEvents (gives the UI live-typing feel)
- **Final results:** Emit `Recognized` events as `isFinal=true` TranscriptEvents

**SignalR push path:**
- Inject `IHubContext<PipelineHub>`
- Send to group `call:{callId}` using method name `PipelineContract.StreamNames.Transcript` ("stream.transcript")
- The UI already subscribes to this stream — no frontend changes needed

**Coexistence with scripted feed:**
- When `AudioSource__Mode=Mock`, the consumer resolves `MockAudioSource` and produces nothing (MockAudioSource yields no frames by default). The scripted feed continues working via its REST endpoints.
- When `AudioSource__Mode=Acs`, the consumer produces real transcripts. The scripted feed REST endpoints still return canned data (they're separate). The UI should be pointed to SignalR for live mode. **No conflict — they coexist cleanly.**
- Future: a `DemoSafety:DataMode` guard could disable the scripted feed entirely. Out of scope now.

**Registration:**
```csharp
services.AddHostedService<SpeechTranscriptionService>();
```
Add to `AddCallCenterServices`. The service self-gates: if IAudioSource yields no frames, it idles.

**DI Note on Program.cs guard:** The existing `DemoSafety:DataMode != Mock → throw` guard in Program.cs must be REMOVED or relaxed before Mode=Acs works. That guard currently blocks startup if DataMode isn't Mock. Decision: remove it — it was a safety net for Phase 1 scripted mode that is now superseded by the AudioSource:Mode DI swap. The consumer IS the live provider it was waiting for.

---

## Decision 4: Speech Resource + RBAC

### Resource status: ALREADY PROVISIONED

`infra/main.bicep` line 182–194 provisions:
- **Name:** `speech-cctrans-{uniqueSuffix}` (kind: `SpeechServices`, region: `swedencentral`, SKU: S0)
- **Custom subdomain:** `speechcctrans{uniqueSuffix}`

### RBAC already assigned:

`infra/main.bicep` lines 422–429 (`apiToSpeechRoleAssignment`) assigns:
- **Role:** `Cognitive Services User` — GUID `a97b65f3-24c7-4388-baec-2e87135dc908`
- **Scope:** The Speech resource only
- **Principal:** ACA system-assigned managed identity

### GUID verification:

`a97b65f3-24c7-4388-baec-2e87135dc908` is the well-known built-in role "Cognitive Services User" — grants data-plane access (read predictions, list keys is NOT included). This is the correct least-privilege role for calling Speech STT. It exists in all Azure directories (it's a global built-in, unlike the ACS role we got burned on).

**However** — given the ACS RBAC lesson: **Meyrin MUST verify before relying on it:**
```bash
az role definition list --name "Cognitive Services User" --query "[].{name:name, id:id}" -o table
```
If it returns the GUID, proceed. If not (unlikely but verify), use `Cognitive Services Speech User` (`f2dc8367-1007-4938-bd23-fe263f013447`) as fallback — but verify that GUID too.

### Is the role assignment LIVE on the running app?

The Bicep assignment exists, but the live app was created via surgical `az` after an ACS recreate — the role assignment may or may not have survived. **Meyrin must verify:**
```bash
az role assignment list --assignee <ACA-system-MI-principalId> --scope <speech-resource-id> --query "[].roleDefinitionName"
```
If missing, apply surgically:
```bash
az role assignment create --assignee <principalId> --role "Cognitive Services User" --scope <speech-resource-id>
```

---

## Decision 5: Scope Guard — In/Out + Go-Live Sequence

### IN (this round):

1. ✅ `SpeechTranscriptionService` (BackgroundService, continuous recognition, push stream, SignalR output)
2. ✅ Event Grid system topic + IncomingCall subscription (webhook, no Entra auth)
3. ✅ Surgical `az` to set `minReplicas=1` + `AudioSource__Mode=Acs`
4. ✅ Verify Speech RBAC is live (and ACS RBAC)
5. ✅ Remove/relax `DemoSafety:DataMode` startup guard in Program.cs
6. ✅ End-to-end test: call +18774178275 → see live transcript in rep UI via SignalR
7. ✅ Bicep consistency: add Event Grid system topic + subscription to `main.bicep` (non-blocking; surgical az is the activation path)

### OUT (explicitly deferred):

- ❌ Entra-protected Event Grid delivery auth (revisit if this ever moves past POC)
- ❌ Diarization/speaker-id from Speech (use "unknown" speaker initially; enrich later)
- ❌ Full `azd provision` / reconstructing the azd environment
- ❌ Translation, sentiment, churn-risk, NBA, knowledge cards from LIVE audio (those stay scripted)
- ❌ AddParticipant (rep join) — Phase 2
- ❌ Reconnect on dropped WebSocket — POC limitation (restart call)
- ❌ Multi-replica / sticky sessions
- ❌ PSTN number rotation / multiple numbers
- ❌ Production-grade error recovery / retry logic

### Go-Live Sequence (end-to-end):

```
1. [Lacus] Build SpeechTranscriptionService against IAudioSource + PipelineHub SignalR path
2. [Lacus] Remove DemoSafety:DataMode startup guard (or gate it to only block non-Mock when consumer absent)
3. [Meyrin] Verify Speech RBAC + ACS RBAC are live on ACA MI (az role assignment list)
4. [Meyrin] Build + deploy new API image to ACA (includes consumer)
5. [Meyrin] Create Event Grid system topic + subscription (surgical az)
6. [Meyrin] az containerapp update: minReplicas=1, AudioSource__Mode=Acs
7. [Dyakka] Test call to +18774178275 — verify call connects, audio streams, transcript appears in UI
8. [Dyakka] Document demo runbook: how to trigger a call, expected latency, what to do if it fails
```

### Demo-honesty fallback:

- `MockAudioSource` remains registered and default. If ANYTHING in the live path fails during a demo, flip `AudioSource__Mode=Mock` (30-second recovery via `az containerapp update --set-env-vars AudioSource__Mode=Mock`) and demo using the scripted propane feed.
- The scripted feed REST endpoints always work regardless of mode.
- Dyakka owns the demo runbook including this fallback procedure.

---

## VERDICT: APPROVE TO BUILD

**Guardrails:**

1. Do NOT flip `AudioSource__Mode=Acs` until Steps 1–5 in the go-live sequence are confirmed green.
2. Meyrin verifies ALL role GUIDs against the live subscription before assigning (lesson from the ACS RBAC burn).
3. The `DemoSafety:DataMode` guard removal must be code-reviewed — it's a safety net being retired; confirm no other code depends on it.
4. Event Grid subscription creation will trigger the SubscriptionValidationEvent handshake — the endpoint MUST be live and responsive at creation time (i.e., the new image with consumer must be deployed first, OR the existing image already handles validation — it does ✅).
5. If the Speech SDK + push stream approach hits a blocker (e.g., token auth not supported in the native SDK version), fallback to REST-based batch recognition is acceptable but degraded (no interim results). Flag immediately if this happens.

**Owners:**
- **Lacus:** SpeechTranscriptionService (the consumer) + DemoSafety guard removal
- **Meyrin:** Event Grid provisioning + RBAC verification + deploy + surgical az flip
- **Dyakka:** End-to-end call test + demo runbook
- **Athrun:** Gate review on the consumer PR before deploy
