# ACS Assessment and Recommended Plan
**By:** Dyakka — ACS / Telephony Specialist  
**Date:** 2026-06-08T14:05:26.525-04:00  
**Type:** Decision Proposal — Assessment + Plan (no code/infra changes)  
**Status:** Inbox — awaiting Jason's steering decisions

---

## 1. Where ACS Stands Today

### What exists
- **ACS resource**: Provisioned in Bicep (`Microsoft.Communication/communicationServices`, global / `dataLocation: Europe`). ✅
- **ACS endpoint env var**: Already wired as `Acs__Endpoint` into the ACA Container App. ✅
- **`IAudioSource` / `AudioFrame` contract**: Defined in `CallCenterTranscription.Telephony`. Clean. `IAsyncEnumerable<AudioFrame> ReadAsync(CancellationToken)`. `AudioFrame` carries `TimestampUtc`, `Encoding` ("pcm16"), `SampleRateHz` (16000), `byte[] Payload`. ✅
- **`MockAudioSource`**: Registered in DI; yields one silent 4-byte frame. Fallback is in place. ✅
- **ACA public HTTPS ingress**: External ingress, `transport: auto`, port 8080. WSS promoted automatically. Public URL available post-deploy. ✅

### What is missing
- **`AcsAudioSource`**: Does not exist. Zero code. No Call Automation SDK reference anywhere. ❌
- **IncomingCall webhook route** (`POST /api/events/acs/incoming-call`): Not in `Program.cs`. ❌
- **Media-streaming WebSocket route** (`wss://.../api/calls/media-stream`): Not in `Program.cs`. ❌
- **Event Grid subscription**: Intentionally deferred in Bicep with a TODO comment. ❌
- **ACS data-plane RBAC**: No role assignment from ACA system identity to ACS resource in Bicep. ❌
- **Phone number (PSTN)**: Not provisioned. Requires portal/API purchase + billing eligibility check for Sweden. ❌
- **`minReplicas = 1`**: ACA currently at `minReplicas = 0`. A cold start on an inbound call (30-second ring window) will drop the call. ❌

### Critical hidden gap — IAudioSource is registered but never called
`IAudioSource` is wired in DI but nothing in the API reads from it. The entire current "pipeline" is the deterministic scripted propane-retention feed (`ScriptedPropaneRetentionScenarioFeed`). `PipelineHub` is SignalR group wiring only. **There is no audio → Speech → transcript → AI flow yet.** That background service is Lacus + Meyrin territory and must be built alongside or before the full live ACS path matters end-to-end.

---

## 2. Target Call Path (End-to-End for Demo)

```
Customer phone call
        │
        ▼
[ACS PSTN number]
        │ IncomingCall event
        ▼
[Event Grid] ──── POST /api/events/acs/incoming-call ────▶ [ACA API]
                                                                │
                                                         Answer call
                                                         (Call Automation SDK,
                                                          managed identity)
                                                                │
                                                         Start media streaming
                                                         → wss://<aca>/api/calls/media-stream
                                                                │
                                                         [WebSocket handler]
                                                         deserialize ACS frames
                                                                │
                                                         AcsAudioSource.ReadAsync()
                                                         emits AudioFrame
                                                         (pcm16, 16kHz, mono)
                                                                │
                                               [Background service — Lacus+Meyrin]
                                               feeds PCM to Azure AI Speech SDK
                                                                │
                                                         Transcript events
                                                                │
                                               AI reasoning (IReasoningClient)
                                                                │
                                               SignalR push → Rep dashboard
                                                                │
                                                        [Rep dashboard UI]
```

**Rep join options (two-party call):**
- **(A — PSTN)** Backend calls `AddParticipant` with rep's phone number after answering. Rep gets a phone call.
- **(B — ACS web client)** Rep joins via ACS Calling SDK in browser. No second phone number needed; heavier setup.

**Audio format contract (must match Lacus's Speech SDK input):**
- PCM 16-bit mono, **16,000 Hz**, 640 bytes/frame, 20 ms packets, 50 fps.
- Mixed audio (all participants flattened into one stream) — per team decision as the POC starting point.
- Unmixed (per-participant streams) is Phase 2 if diarization from the stream is needed.

---

## 3. Hard Prerequisites and Blockers

| Prerequisite | Status | Notes |
|---|---|---|
| ACS resource | ✅ Provisioned | global / Europe data location |
| ACS data-plane RBAC on ACA identity | ❌ Missing | Need `Communication Services Contributor` or narrower role in Bicep |
| ACS phone number (PSTN) | ❌ Not purchased | Portal or API; billing eligibility for Sweden must be verified |
| `minReplicas = 1` on ACA during demo | ❌ Currently 0 | Drop-call risk; one-line Bicep param change |
| Event Grid system topic + subscription | ❌ Deferred | Safe to add once webhook route is implemented and validated |
| IncomingCall webhook route + SubscriptionValidationEvent | ❌ Not coded | Must also secure endpoint (Entra webhook auth or HMAC secret via Key Vault) |
| Media-streaming WebSocket route | ❌ Not coded | ACA `transport: auto` already promotes WSS |
| `AcsAudioSource` class | ❌ Not coded | My deliverable |
| Audio → Speech background service | ❌ Not coded | Lacus + Meyrin deliverable; dependency for live transcript |
| Managed identity auth to ACS (no connection string) | ✅ Planned / no secrets in code | Must be implemented when AcsAudioSource is coded |

**Cost / provisioning items that need Jason's decision:**
- Swedish PSTN number: monthly rental fee + per-minute charges. Instant to provision in portal for US; Sweden eligibility depends on subscription billing country.
- Event Grid delivery: minimal cost for volume of a POC.
- ACS Call Automation + media streaming: billed per minute of call + per minute of media streaming; POC rehearsal costs are low.

---

## 4. Recommended Phased Plan

### Phase 1 — Infra prereqs (Bicep + Portal)
**Type:** Infra (Bicep) + manual portal action  
**Blocks:** Everything real

1. **Bicep**: Add ACS data-plane RBAC role assignment — ACA system identity → ACS resource (`Communication Services Contributor` or `Communication Services Call Automation Client`; exact role to confirm with Athrun).
2. **Bicep param**: `minReplicas = 1` on ACA Container App (add as a param, default 0 for cost, set to 1 for demo window; or document the az CLI command to scale up before demo).
3. **Portal / manual**: Purchase Swedish ACS phone number. (Verify billing eligibility first — if blocked, fall back to Option B or C.)
4. **Bicep**: Add Event Grid system topic + subscription for `IncomingCall` → ACA webhook. **Gate:** only do this after Phase 2 routes are live and validated (per existing TODO in Bicep).

### Phase 2 — API routes (Code)
**Type:** Code — my coordination with Meyrin  
**Blocks:** Real inbound call + media streaming start

1. `POST /api/events/acs/incoming-call` — handle `SubscriptionValidationEvent` + `IncomingCall`. Use Call Automation SDK (managed identity) to answer the call, start media streaming to `wss://<self>/api/calls/media-stream`. Secure via Microsoft Entra-protected webhook or HMAC secret stored in Key Vault (not in code).
2. `GET /api/calls/media-stream` — WebSocket upgrade endpoint. Accepts ACS JSON frames (`AudioMetadata`, `AudioData`), deserializes, hands PCM payload to `AcsAudioSource` channel.

### Phase 3 — AcsAudioSource (Code)
**Type:** Code — my deliverable  
**Blocks:** Live audio into the interface (unblocks Lacus for audio→Speech wiring)

- Implement `AcsAudioSource : IAudioSource` in `CallCenterTranscription.Telephony`.
- Uses a `Channel<AudioFrame>` internally; WebSocket handler writes, `ReadAsync()` reads.
- Deserializes ACS `AudioMetadata` (validates 16kHz/pcm16), decodes base64 `AudioData.Data` to `byte[]`, emits `AudioFrame`.
- NuGet to add at implementation time: `Azure.Communication.CallAutomation` (current GA).
- DI registration: config flag `AudioSource:Mode = "Acs" | "Mock"` — one swap, no rebuild.
- No connection strings. `DefaultAzureCredential` (managed identity) for CallAutomationClient.

### Phase 4 — Audio pipeline coordination (Coordination)
**Type:** Coordination with Lacus + Meyrin; architecture sign-off from Athrun

- **Lacus**: Confirm audio format handshake — mixed PCM 16kHz/16-bit/mono as Speech SDK input; confirm whether interim hypotheses should be filtered and only final results pushed downstream.
- **Meyrin**: Background service (`IHostedService`) that calls `audioSource.ReadAsync()` and feeds `AudioFrame.Payload` bytes to Azure AI Speech SDK for real-time transcription. This service is the final link. Without it, `AcsAudioSource` is implemented but transcript events are still scripted.
- **Athrun**: Architecture sign-off on the WebSocket host topology (single ACA instance during demo; reconnect handling if ACA restarts mid-call).

---

## 5. Options for How Far to Go

### Option A — Full real PSTN inbound call (end-to-end)
**What it delivers:** Customer dials a real Swedish phone number → ACS answers → media streaming → `AcsAudioSource` → Speech → transcript → dashboard.  
**Cost:** Phone number monthly fee + per-minute call + media-streaming minutes. Low for a POC.  
**Risk:** Swedish billing eligibility must be confirmed. Number provisioning is instant once eligibility is cleared. Everything else follows from Phases 1–4.  
**Demo impact:** Maximum realism. Full dual-party rep+customer scenario.

### Option B — ACS web call (no PSTN number)
**What it delivers:** Both rep and customer join via the ACS Calling SDK web client in a browser. Call Automation `Connect` action attaches to the server call. Media streaming works identically.  
**Cost:** No PSTN number cost. ACS web calling is billed differently (lower).  
**Risk:** Requires building an ACS Calling SDK web-client join flow (more frontend work; Lunamaria involvement). More setup for the demo operator.  
**Demo impact:** Still two real humans with real audio, just over WebRTC not PSTN. Convincing enough.

### Option C — Media-streaming plumbing, mock audio active (default recommended)
**What it delivers:** Implement `AcsAudioSource` + WebSocket handler + IncomingCall webhook code. DI stays on `MockAudioSource` until a real call is configured. Lacus + Meyrin can build and test the audio→Speech pipeline against a real interface. Demo runs reliably from the scripted feed as fallback.  
**Cost:** Zero until a phone number is purchased.  
**Risk:** Lowest. No Azure provisioning decisions needed immediately.  
**Demo impact:** No live audio yet, but the full integration is code-complete and one config swap away from going live once a number is provisioned.

### ✅ Recommended default: Option C → then Option A
Build the plumbing first (Option C). This unblocks Lacus + Meyrin and lets us rehearse the full pipeline with real Speech SDK against recorded audio. Once Swedish billing eligibility is confirmed, provision the number and flip to Option A for the live demo. Keep `MockAudioSource` registered as the fallback path always.

---

## 6. Open Decisions Needed from Jason

1. **Buy a Swedish PSTN number? (y/n)** — If yes, I need confirmation that the Azure subscription billing country is eligible for Swedish numbers. If no, do we go Option B (ACS web call) or Option C only?
2. **Real inbound vs. ACS web call?** — Option A (PSTN) or Option B (browser-to-browser via ACS Calling SDK)? This determines whether Lunamaria needs to build a web-calling join flow.
3. **How live should the demo be?** — Full end-to-end real audio (A or B), or plumbing-complete + mock audio (C)?
4. **`minReplicas = 1` as a permanent Bicep default or a manual az CLI step before demo?** — Permanent is safer; adds ~$15–30/month for a tiny ACA instance.
5. **Event Grid webhook security method** — Microsoft Entra-protected webhook (preferred; more complex to configure) or HMAC shared secret stored in Key Vault (simpler for POC)?
6. **ACS RBAC role to assign** — `Communication Services Contributor` covers everything but is broad. Needs Athrun sign-off on which built-in role is appropriate for the POC scope.
