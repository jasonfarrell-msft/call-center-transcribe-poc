# Dyakka — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** Real Azure Communication Services integration — inbound call answering, media streaming to fork live call audio into the pipeline, and a repeatable **dual-party call script** (rep + customer both on a call ACS can capture) for the demo. I implement `AcsAudioSource` behind Athrun's `AudioSourceProvider`.
- **Hired:** 2026-06-05 by the owner, specifically to solve "how do two people call into ACS and we fork the audio."
- **Stack context:** Backend is **C#/.NET** on Azure Container Apps; use the ACS .NET SDK (Call Automation + media streaming). Managed identity, no secrets in code.
- **Created:** 2026-06-05.

## Learnings

### 2026-06-05 — ACS Audio Streaming & Call Topology Research

**Audio Streaming (GA in Call Automation .NET SDK):**
- ACS Call Automation supports **Start/Stop audio streaming** to a WebSocket endpoint — confirmed GA across .NET, Java, JS, Python SDKs.
- Supports **Mixed** (all participants flattened into one stream) and **Unmixed** (per-participant, up to 4 dominant speakers with `participantRawID`).
- Audio format: **PCM 16-bit mono** at **16,000 Hz** (default) or 24,000 Hz. Frame rate 50 fps, 20ms packets, 640 bytes/frame at 16kHz.
- Bidirectional streaming supported (`EnableBidirectional = true`) — we only need the inbound direction for STT.
- WebSocket receives JSON messages: first `AudioMetadata` (encoding, sampleRate, channels, length), then `AudioData` (base64 PCM, timestamp, participantRawID, silent flag).
- Source: https://learn.microsoft.com/en-us/azure/communication-services/concepts/call-automation/audio-streaming-concept

**Call Topology — Recommended for POC:**
- **Option A (RECOMMENDED): Inbound PSTN → Call Automation answers → AddParticipant (rep).** Customer dials the ACS-acquired phone number. Our backend receives `IncomingCall` via Event Grid, answers with Call Automation SDK, starts media streaming, then adds the rep as a second PSTN participant (or ACS identity via Calling SDK web client). Two-party call with audio streaming to our WebSocket.
- Option B (Group Call / Rooms + Connect): More complex; requires both parties to use ACS SDKs or Connect action. Viable but heavier for a POC.
- Option C (Connect to existing call): Requires an existing server call ID. Adds complexity.

**Authentication:**
- Call Automation SDK supports **Microsoft Entra ID / Managed Identity** authentication. No connection strings needed in code.
- The Calling SDK (client-side for rep) uses **User Access Tokens** generated server-side via Identity SDK.

**Event Grid & Callbacks:**
- `IncomingCall` event delivered via Event Grid subscription (Webhook type).
- All mid-call events (CallConnected, AddParticipantSucceeded, MediaStreamingStarted, etc.) delivered to the callback URI specified at answer time.
- Backend needs a **publicly reachable HTTPS endpoint** for both the Event Grid webhook and the WebSocket server (ACA ingress handles this; dev tunnels/ngrok for local dev).
- Best practice: Event Grid retry policy → Max 2 attempts, TTL 1 minute (call rings 30s max).

**Prerequisites for the POC:**
1. ACS resource in Azure.
2. Acquired PSTN phone number (US toll-free or local; instant provisioning in portal for US).
3. Event Grid subscription on the ACS resource filtering `IncomingCall` events → backend webhook.
4. Backend (ACA) with public ingress exposing: (a) webhook for Event Grid/callbacks, (b) WebSocket endpoint for audio streaming.
5. Managed Identity on ACA assigned `Contributor` role on ACS resource (or use ACS-specific RBAC roles).

**Demo Runbook Outline (Dual-Call Script):**
1. Operator starts backend (ACA deployed or local via dev tunnel).
2. Operator opens rep dashboard (Razor frontend).
3. "Customer" (demo partner or second phone) dials the ACS phone number.
4. Backend receives IncomingCall → answers → starts media streaming → WebSocket audio flows.
5. Backend calls AddParticipant with the rep's phone number (or rep joins via ACS web calling client).
6. Both parties connected; audio streaming active; pipeline receives PCM frames.
7. Dashboard shows live transcript, sentiment, churn signals, NBA.
8. **Fallback:** If PSTN call fails, operator toggles `AudioSourceProvider` to `MockAudioSource` (pre-recorded WAV) — pipeline continues identically.

**Risks & Gotchas:**
- PSTN number provisioning is instant for US in portal, but may require special order for other regions.
- ACA must have a stable public URL (custom domain or default `*.azurecontainerapps.io`).
- WebSocket endpoint must be wss:// (TLS). ACA ingress provides this automatically.
- Media streaming known limitation: stopping with a different operationContext doesn't update the context in the event.
- Call rings for only 30 seconds — answer must be fast.
- Unmixed audio gives cleaner speaker separation but limited to 4 dominant speakers. Mixed is simpler; we have diarization downstream anyway.

### 2026-06-06 — Sweden Central real-call resource nuance

- Treat **Azure Communication Services** as a special case: the resource is configured with a geography / data location rather than Sweden-Central compute placement. For this POC, keep the regional pieces (`ACA`, optional `UAMI`, logging) in `swedencentral`, but do **not** describe ACS itself as Sweden-Central-hosted without explicit product documentation.
- With ACS, the **Event Grid system topic is global**, not Sweden Central. The webhook still needs public HTTPS, `SubscriptionValidationEvent` handling, and its own authentication layer (Microsoft Entra-protected webhook or a shared secret); endpoint validation alone is not enough.
- Swedish ACS phone numbers can receive PSTN calls, but only on a **paid** subscription whose **billing location** is in the documented eligible-country list. Verify billing-country eligibility before promising a Sweden number for the demo.
- For demo reliability on Azure Container Apps, keep the telephony API at **minReplicas = 1** during demo windows; inbound PSTN calls only ring for ~30 seconds, so cold-start risk is unnecessary.

### 2026-06-08 — ACS Opening Assessment (current repo state + recommended plan)

**Current ACS state (as of 2026-06-08):**
- **IAudioSource contract** (`CallCenterTranscription.Telephony`): Minimal and clean — `IAsyncEnumerable<AudioFrame> ReadAsync(CancellationToken)`. `AudioFrame` carries `TimestampUtc`, `Encoding` (default "pcm16"), `SampleRateHz` (default 16000), `byte[] Payload`. No ACS SDK refs anywhere in the Telephony .csproj (no NuGet packages at all).
- **MockAudioSource**: Exists — yields one silent 4-byte PCM frame; registered in DI as `IAudioSource` singleton. This is the only implementation.
- **No `AcsAudioSource`**: Does not exist. No Call Automation answer logic, no IncomingCall webhook, no WebSocket media-streaming handler anywhere in `src/`.
- **Critical gap — IAudioSource is NEVER called**: `IAudioSource` is DI-registered but nothing in the API actually calls `ReadAsync()`. The entire "pipeline" is the scripted propane-retention feed (`ScriptedPropaneRetentionScenarioFeed`). PipelineHub is SignalR group wiring only. There is no audio → Speech → transcript → AI flow yet; that is Lacus + Meyrin territory and must be built in parallel.
- **Infra state**: ACS resource IS provisioned in Bicep (`Microsoft.Communication/communicationServices`, global/Europe data location). ACS endpoint is already wired as an env var (`Acs__Endpoint`) on the ACA Container App. **Missing**: phone number asset, Event Grid subscription, ACS data-plane RBAC role assignment for the ACA system identity, `minReplicas=1` during demo. Event Grid intentionally deferred with a TODO comment until API routes exist.
- **ACA public ingress**: External HTTPS (`transport: auto`, port 8080) — WebSocket (WSS) promoted automatically by ACA ingress. Public URL available immediately after deploy. `minReplicas=0` currently (must be raised to 1 for demo).

**Target call path (end-to-end for demo):**
1. Customer dials ACS PSTN number → ACS fires `IncomingCall` via Event Grid → ACA webhook (`POST /api/events/acs/incoming-call`) — requires SubscriptionValidationEvent handling + Entra/secret auth on webhook.
2. API answers call using Call Automation SDK (managed identity, no connection string) → starts media streaming to `wss://<aca-fqdn>/api/calls/media-stream`.
3. ACS streams PCM AudioData frames over WebSocket → `AcsAudioSource` receives JSON messages, decodes base64 payload, emits `AudioFrame` (pcm16, 16000 Hz) into `IAsyncEnumerable<AudioFrame>`.
4. A background service (Lacus + Meyrin territory) reads from `IAudioSource` → feeds frames to Azure AI Speech SDK → transcript events.
5. Transcript → AI reasoning → SignalR push to dashboard.
6. Rep joins as second participant: either added via `AddParticipant` (PSTN rep number) or joins via ACS Calling SDK web client.
- **Audio format**: PCM 16-bit mono, 16,000 Hz, 640 bytes/frame, 50 fps/20 ms packets. Mixed audio (all participants flattened) is the POC starting point per team decision. Must confirm this matches Lacus's Speech SDK input expectations before implementation.

**Key blockers / prereqs for real call:**
1. A Swedish ACS phone number — portal/API purchase; billing eligibility for Sweden must be verified against Azure subscription billing country.
2. `minReplicas=1` on ACA during demo windows (Bicep change).
3. ACS data-plane RBAC role on ACA system identity (`Communication Services Contributor` or narrower role; to be confirmed).
4. Event Grid system topic + subscription wiring in Bicep (deliberately deferred; safe to add once routes exist).
5. API routes: `POST /api/events/acs/incoming-call` (Event Grid webhook with SubscriptionValidationEvent handler + secure endpoint auth), `GET /ws /api/calls/media-stream` (WebSocket upgrade endpoint).
6. `AcsAudioSource` class in `CallCenterTranscription.Telephony`.
7. Audio → Speech → transcript pipeline wiring (Lacus + Meyrin; a dependency for the full live path but not for implementing AcsAudioSource itself).
8. Public callback URL is already available via ACA external ingress — no ngrok needed for deployed path.

**Recommended phased plan:**
- **Phase 1 (Infra prereqs)**: Bicep: add ACS RBAC role assignment for ACA system identity; add `minReplicas=1` param; add Event Grid system topic + subscription (gated by API route validation). Portal: purchase Swedish PSTN number.
- **Phase 2 (Code — API routes)**: Add `POST /api/events/acs/incoming-call` (Event Grid SubscriptionValidationEvent + IncomingCall handler; answer call; start media streaming) + `GET /ws /api/calls/media-stream` (WebSocket upgrade; ACS audio frames in). Secure with Entra webhook auth or HMAC secret from Key Vault.
- **Phase 3 (Code — AcsAudioSource)**: Implement `AcsAudioSource : IAudioSource` — receives binary WebSocket messages, deserializes ACS AudioMetadata/AudioData JSON, base64-decodes PCM payload, emits `AudioFrame` objects. DI-swap via config flag (mock vs. real).
- **Phase 4 (Coordination — audio pipeline)**: Coordinate with Lacus (audio format handshake: 16kHz PCM mixed confirmed?), Meyrin (background service that reads IAudioSource and feeds Speech SDK). This is the final link to live transcript.

**Options recommended to Jason:**
- **Option A (Full real PSTN)**: Buy Swedish number, wire everything, live inbound call end-to-end. Highest demo impact; requires billing eligibility check + number provisioning.
- **Option B (ACS web call, no PSTN number)**: Both rep and customer join via ACS Calling SDK from browser (no phone number needed). Cheaper/faster to provision; slightly heavier on client setup but avoids PSTN billing. Still uses Call Automation for media streaming.
- **Option C (Media-streaming plumbing only, mock audio)**: Implement the WebSocket handler and AcsAudioSource code, keep `MockAudioSource` active in DI, defer phone number purchase. Lets Lacus + Meyrin build/test the audio→Speech pipeline against the interface without needing a real call. Lowest risk; demo still runs from mock.
- **Recommended default**: Option C first (unblock the pipeline), then Option A or B once billing eligibility is confirmed.

### 2026-06-08 — Option C ACS Plumbing Built

**What was built (Option C per Athrun's sign-off):**

**AcsAudioSource design (Telephony project):**
- `AcsAudioSource : IAudioSource` in `CallCenterTranscription.Telephony`, backed by a `System.Threading.Channels.Channel<AudioFrame>` (bounded capacity 1000, `FullMode = DropOldest`, `SingleReader = true`, `SingleWriter = true`).
- `HandleWebSocketMessageAsync(byte[] rawMessage, CancellationToken)` parses ACS JSON messages: `AudioMetadata` (logged, no frame) and `AudioData` (base64 PCM decoded → `AudioFrame{pcm16, 16000Hz}` written via `TryWrite`). Malformed JSON and invalid base64 are caught and logged as warnings — never thrown.
- `CompleteStream()` calls `channel.Writer.TryComplete()` to signal end-of-stream to `ReadAsync` consumers. No reconnect logic (POC; documented limitation).
- `ReadAsync(CancellationToken)` uses `channel.Reader.ReadAllAsync()` as `IAsyncEnumerable<AudioFrame>` — exact IAudioSource contract match.

**Two routes added to `CallCenterTranscription.Api` (via `AcsEndpoints.MapAcsRoutes()`):**
1. `POST /api/events/acs/incoming-call` — Event Grid webhook. Handles `Microsoft.EventGrid.SubscriptionValidationEvent` (echoes `validationCode`) and `Microsoft.Communication.IncomingCall` (answers call via `CallAutomationClient` + starts media streaming to `wss://{host}/api/calls/media-stream`). **Excluded from JWT auth policy** — placed in a separate `app.MapGroup("/api/events/acs").AllowAnonymous()` group.
2. `POST /api/events/acs/callbacks` — ACS mid-call event callbacks (CallConnected, etc.); returns 200 OK. Same anonymous group.
3. `GET+WS /api/calls/media-stream` — `app.Map(...).AllowAnonymous()`. Accepts WebSocket upgrade, reads fragmented ACS frames into a MemoryStream, calls `acsSource.HandleWebSocketMessageAsync()`. On close, calls `acsSource.CompleteStream()`. Pre-existing `app.UseWebSockets()` added early in the middleware pipeline.

**DI swap mechanism (`ServiceCollectionExtensions.cs`):**
- `AddCallCenterServices(IServiceCollection, IConfiguration)` — now takes `IConfiguration`.
- `AcsAudioSource` is ALWAYS registered as a singleton (so the WebSocket handler can inject it in any mode).
- `AudioSource:Mode` (env: `AudioSource__Mode`) = `"Mock"` (default) → `IAudioSource = MockAudioSource`; `"Acs"` → `IAudioSource = AcsAudioSource` (same instance).
- `CallAutomationClient` registered when `Acs:Endpoint` is configured; uses `DefaultAzureCredential` (managed identity). No connection strings.
- SDK added: `Azure.Communication.CallAutomation` 1.5.1 to Telephony.csproj; `Azure.Identity` 1.21.0 to Api.csproj.

**SDK API note for future (1.5.1 `MediaStreamingOptions`):**
- Constructor: `new MediaStreamingOptions(MediaStreamingAudioChannel.Mixed, StreamingTransport.Websocket)`
- Transport URI set as property: `.TransportUri = mediaStreamUri`
- Content set as property: `.MediaStreamingContent = MediaStreamingContent.Audio`
- Start immediately: `.StartMediaStreaming = true`
- The old 4-arg constructor (with `MediaStreamingTransport` enum) no longer exists in 1.5.1.

**Auth exclusion for webhook:**
- The `AgentAssistAccess` JWT policy is on the `app.MapGroup("/api")` route group.
- ACS routes are a separate `app.MapGroup("/api/events/acs").AllowAnonymous()` group + `app.Map("/api/calls/media-stream").AllowAnonymous()`.
- Even when `Security:RequireAuth=true`, these routes do NOT require a Bearer token.
- TODO next round: Meyrin must add Microsoft Entra delivery authentication to the Event Grid subscription before going live.

**How to flip to live (next round):**
1. Set env var `AudioSource__Mode=Acs` on ACA Container App.
2. Ensure `Acs__Endpoint` is configured (already is in current infra).
3. Provision ACS PSTN phone number (portal).
4. Meyrin: add ACS RBAC role assignment + `minReplicas=1` Bicep param + Event Grid subscription + Entra delivery auth.
5. Test: `POST /api/events/acs/incoming-call` with a synthetic SubscriptionValidationEvent body; verify 200 + validationResponse. Then wire real Event Grid sub. Dial the number.
