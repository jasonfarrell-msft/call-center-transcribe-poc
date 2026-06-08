# Dyakka — History

## Current Focus (2026-06-08)

### ACS Option C Plumbing — COMPLETE ✅

Delivered **Option C** (media-streaming plumbing + mock audio default):

**Code delivered:**
- `AcsAudioSource : IAudioSource` — Channel-backed, deserializes ACS JSON frames (AudioMetadata logged, AudioData base64→PCM)
- Routes: `POST /api/events/acs/incoming-call` (SubscriptionValidationEvent + IncomingCall), `POST /api/events/acs/callbacks`, WebSocket `/api/calls/media-stream`
- DI swap: `AudioSource:Mode` config (default `"Mock"` preserves existing behavior; flip to `"Acs"` for live path)
- NuGet: `Azure.Communication.CallAutomation` 1.5.1 GA, `Azure.Identity` 1.21.0
- **Build:** 0 errors, 0 warnings, 25/26 tests pass (1 pre-existing UI failure)

**Dormant until live flip:**
- All routes (no Event Grid, no phone number)
- AcsAudioSource Channel (empty in Mock mode)
- CallAutomationClient (registered but never called)

**Key decisions (Athrun signed off):**
- RBAC: `Communication Services Contributor` scoped to ACS resource on ACA system identity
- minReplicas: 1 (prevents cold-start call drops; ~$15–30/month cost negligible for POC)
- Webhook security: SubscriptionValidationEvent handshake + schema validation (Entra auth deferred to Event Grid wiring)
- Auth: `DefaultAzureCredential` (managed identity), zero secrets in code

**Residual TODOs (deferred):**
1. PSTN phone number purchase — subscription eligibility check needed
2. Event Grid system topic + subscription — deferred until webhook validated
3. Microsoft Entra delivery auth on Event Grid — blocking for going live
4. ACS RBAC role assignment (Bicep) — **[Meyrin delivered](2026-06-08)**
5. minReplicas = 1 (Bicep param) — **[Meyrin delivered](2026-06-08)**
6. Audio → Speech consumer (IHostedService) — Lacus + Meyrin next round

### US Phone Number Feasibility Advisory — COMPLETE ✅

Jason asked: "Can we use US numbers? Deploy to East US or East US 2?"

**Answer:** YES — but with one critical correction.

**Key distinction:**
- ACS has no per-region deployment. `location` is always `'global'`.
- **`dataLocation`** (data residency, set at create time) controls number geography:
  - `'Europe'` → European numbers only (Swedish, German, etc.)
  - `'United States'` → US toll-free + US geographic

**US number types:**
- **Toll-free (1-800, 1-888):** No US address, no regulatory wait, instant provision. **Recommended for demo.**
- **Geographic:** Requires US address, regulatory form, days–weeks approval.

**Critical detail:** `dataLocation` is IMMUTABLE. In-place update fails. Requires delete + recreate ACS resource.

**Safe to switch now** because:
- No PSTN phone number purchased
- No Event Grid subscription wired
- Zero sunk assets
- Ideal time to flip

**Path forward:**
1. Verify subscription eligibility (portal: ACS → Phone Numbers → Get → search US toll-free; if blocked, subscription type is the issue)
2. Change Bicep: `communicationDataLocation: 'Europe'` → `'United States'`
3. Delete existing ACS resource (required, immutable)
4. `azd provision` (recreates with US residency; RBAC auto-reapplies)
5. Purchase US toll-free (portal)
6. Wire Event Grid + Entra delivery auth (next round)
7. Flip `AudioSource__Mode=Acs` (one env var, no rebuild)

**[Meyrin implemented dataLocation flip (2026-06-08); awaiting operator delete + provision](2026-06-08)**

---

## Archive — Earlier Learnings & Context

### 2026-06-05 — ACS Audio Streaming & Call Topology Research

**Audio Streaming (GA in Call Automation .NET SDK):**
- ACS Call Automation supports Start/Stop audio streaming to WebSocket endpoint (GA across .NET, Java, JS, Python)
- Mixed (all participants flattened) and Unmixed (per-participant, 4 dominant) modes
- PCM 16-bit mono, 16,000 Hz default (or 24,000 Hz), 50 fps (20ms packets, 640 bytes/frame at 16kHz)
- Bidirectional streaming supported (we only need inbound for STT)
- WebSocket: JSON frames with AudioMetadata (encoding, sampleRate, channels) + AudioData (base64 PCM, timestamp, participantRawID, silent flag)

**Call Topology for POC:**
- Inbound PSTN → Call Automation answers → AddParticipant (rep via PSTN or ACS web client) → two-party call with audio streaming to WebSocket
- Alt: Group Call / Rooms + Connect (heavier, deferred)

**Authentication:** Call Automation SDK supports Microsoft Entra ID / Managed Identity (no connection strings).

**Event Grid & Callbacks:**
- IncomingCall event via Event Grid subscription (Webhook)
- Mid-call events (CallConnected, AddParticipantSucceeded, MediaStreamingStarted, etc.) via callback URI specified at answer time
- Requires publicly reachable HTTPS endpoint (ACA ingress handles this)
- Best practice: Event Grid max 2 attempts, TTL 1 minute (call rings 30s max)

**Prerequisites:** ACS resource, PSTN phone number, Event Grid subscription, public ACA ingress with webhook + WebSocket endpoints, managed identity with RBAC role on ACS resource.

**Known gotchas:**
- ACS must have stable public URL (custom domain or default `*.azurecontainerapps.io`)
- WebSocket must be WSS (TLS); ACA ingress automatic
- Call rings 30 seconds only; answer must be fast
- Unmixed audio cleaner for speaker separation but limited to 4 dominant speakers; mixed is simpler

### 2026-06-06 — Sweden Central Real-Call Resource Nuance

- ACS is special: resource configured with geography/data location, not regional compute placement
- Keep regional pieces (ACA, logging) in swedencentral; do not describe ACS as Sweden-Central-hosted without explicit docs
- Event Grid system topic is global, not swedencentral; webhook needs public HTTPS, SubscriptionValidationEvent handling, Entra or shared-secret auth
- Swedish ACS phone numbers require paid subscription with billing location in eligible-country list; verify before promising Sweden number
- Demo reliability: keep telephony API at minReplicas=1 during demo windows (30-second ring risk with cold-start)

### 2026-06-08 Morning — ACS Opening Assessment

**Current repo state (2026-06-08):**
- IAudioSource contract clean: `IAsyncEnumerable<AudioFrame> ReadAsync(CancellationToken)` with AudioFrame{TimestampUtc, Encoding, SampleRateHz, Payload}
- MockAudioSource exists (yields one silent frame); registered as IAudioSource singleton
- **No AcsAudioSource** — does not exist
- **Critical gap:** IAudioSource never called; entire pipeline is scripted propane-retention feed (ScriptedPropaneRetentionScenarioFeed); no audio→Speech→transcript flow yet (Lacus + Meyrin territory)
- Infra: ACS resource provisioned (global/Europe dataLocation), endpoint wired as env var (Acs__Endpoint); missing: phone number, Event Grid subscription, RBAC role assignment, minReplicas=1
- ACA public ingress ready (external HTTPS, WSS automatic), minReplicas=0 (must raise to 1 for demo)

**Options presented to Jason:**
- **Option A (Full real PSTN):** Swedish number, wire everything, live inbound end-to-end. Highest demo impact; requires billing eligibility + number provisioning.
- **Option B (ACS web call, no PSTN):** Both rep and customer join via ACS Calling SDK from browser. Cheaper/faster; no phone number cost; slightly heavier client setup.
- **Option C (Plumbing + mock default, RECOMMENDED):** Implement WebSocket handler, AcsAudioSource, routes; keep MockAudioSource active in DI; defer phone number. Lets Lacus+Meyrin build/test audio→Speech pipeline against the interface without real call. Lowest risk; demo reliable from mock.

**Jason's decisions (captured in decisions.md):**
1. Option C selected (Option C → Option A after billing eligibility confirmed)
2. US dataLocation approved (flip 'Europe' → 'United States')
3. Architect (Athrun) signed off architecture + RBAC + minReplicas + DI swap + webhook security decisions

---

## Prior Sessions (Seed Phase)

**Project seed:** Hired 2026-06-05 to implement real ACS integration — inbound call answering, media streaming fork of live audio, dual-party call script (rep + customer) for demo. Backend C# / .NET on ACA; use ACS .NET SDK (Call Automation + media streaming). Managed identity, zero secrets in code.

**Stack context:** Azure Communication Services for PSTN calling + Call Automation + media streaming; WebSocket endpoint on ACA; Azure AI Speech SDK for STT (Lacus+Meyrin deliverable); AI reasoning downstream. Dashboard live-updates via SignalR.

---

## Summary of Dyakka's Delivered Work

- **Research phase (2026-06-05/06):** ACS audio streaming research, call topology analysis, Swedish region constraints
- **Assessment phase (2026-06-08 morning):** Comprehensive gap analysis of current repo; recommended 4-phase plan; presented 3 options to Jason
- **Implementation phase (2026-06-08):** Option C code delivery — AcsAudioSource + routes + DI swap; build 0/0; 25/26 tests pass
- **Advisory phase (2026-06-08):** US number feasibility analysis; dataLocation immutability explanation; subscription eligibility risk identification
- **Sign-offs:** Athrun approved Option C architecture; Meyrin implemented Bicep infra (RBAC, minReplicas, env var); Athrun approved dataLocation flip

---

## Next Steps

1. Jason/Operator: Delete existing ACS resource (immutable constraint)
2. Operator: Run `azd provision` (ACS recreates with dataLocation=United States)
3. Jason/Operator: Verify subscription eligibility in portal
4. Jason/Operator: Purchase US toll-free number (portal, minutes)
5. Next round (Meyrin+Lacus): Wire Event Grid + Entra delivery auth + build audio→Speech consumer
6. Activation: Flip `AudioSource__Mode=Acs` on ACA env var (one-liner, no rebuild)

---

## Contact / Handoff

All code committed. All decisions documented in `.squad/decisions.md`. Awaiting operator action (ACS resource delete + reprovision).

Dyakka is ready for next round coordination with Lacus + Meyrin on audio→Speech consumer service and Event Grid wiring.
