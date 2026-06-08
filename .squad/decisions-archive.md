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


## 1. Yes — With One Correction

**Short answer: Yes, US numbers are fully supported in ACS and work with Call Automation + media streaming.**

However, "deploying Comm Services to East US or East US 2" is a category error that needs clearing up. ACS is NOT a per-datacenter regional service. The ARM resource `location` is always `'global'` — there is no East US vs. West US deployment slot for ACS the way there is for ACA or App Service. Jason cannot "deploy ACS to East US."

**What actually controls US number availability is `dataLocation`** — ACS's data residency setting, which is specified at resource-creation time. Our Bicep currently has:

```bicep
param communicationDataLocation string = 'Europe'
```

This means our ACS resource is locked to the Europe data geography and can only acquire European numbers (Swedish, German, etc.). To acquire US numbers, `dataLocation` must be `'United States'`.

**The critical distinction:**

| Concept | What Jason said | What actually matters |
|---|---|---|
| Compute region (ACA, App Service) | "Deploy to East US / East US 2" | Irrelevant to number availability |
| ACS data residency | (implied as deployment region) | `dataLocation: 'United States'` — this is the switch |

The rest of the stack — ACA in Sweden Central, App Service in Sweden Central — **does not need to change** at all for US numbers. Only the ACS `dataLocation` matters.

---


## 2. US Number Types, Requirements, and Blockers

### Available US number types

| Type | Inbound + Call Automation + Media Streaming | US Address Required | Regulatory Approval Wait | Demo Suitability |
|---|---|---|---|---|
| **Toll-free** (1-800, 1-888, etc.) | ✅ Yes | ❌ No | None — near-instant | ✅ **Best for demo** |
| **Geographic / local** (area code) | ✅ Yes | ✅ Yes | Days–weeks (regulatory form) | ⚠️ Slower to acquire |

Both types support our exact scenario: inbound PSTN → Call Automation answer → media streaming → `AcsAudioSource`. Both carry voice calling capability (not just SMS).

**Recommendation: toll-free.** No US address, no regulatory wait, provisions in minutes in the portal once the resource has the right `dataLocation`. Perfectly credible for a call center demo.

### Potential blockers

1. **Subscription type eligibility** — This is the real risk.
   - **Trial, free-credit, MSDN, or BizSpark subscriptions:** Typically blocked from purchasing ACS phone numbers entirely.
   - **Pay-as-you-go (PAYG), MCA, EA:** Generally eligible.
   - **How to verify:** In the Azure portal, navigate to the ACS resource → **Phone Numbers** → **Get Phone Number** → search for US toll-free. If the subscription is ineligible, the portal will surface an error immediately (no numbers will appear, or you'll see an explicit subscription eligibility message).
   - I cannot see the subscription type from here — Jason must check this in the portal.

2. **`dataLocation` must be `'United States'`** — Our current resource has `'Europe'`, which blocks US number purchase outright. See section 3.

3. **Billing country** — For US numbers, the subscription billing country does NOT need to be US. The `dataLocation` of the ACS resource is the controlling factor. (Note: this differs from some other ACS geographies like Sweden, where billing-country eligibility is more restrictive.)

---

