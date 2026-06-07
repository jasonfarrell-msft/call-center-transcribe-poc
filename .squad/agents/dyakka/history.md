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
