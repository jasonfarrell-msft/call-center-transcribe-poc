# ACS Final Demo Topology Spike (Issue 090)

This document validates the current ACS wiring against the final demo path and captures the minimum implementation-ready shape for inbound customer calls, media streaming, and rep join.

## 1) Chosen topology (Option A) and demo call flow

The selected demo path remains **Option A: inbound PSTN customer call answered by Call Automation, then rep is added as a participant**.

Flow (current code path):

1. Event Grid sends `Microsoft.Communication.IncomingCall` to `POST /api/events/acs/incoming-call`.
2. API answers the call and starts ACS media streaming to `wss://<api-host>/api/calls/media-stream`.
3. ACS emits mid-call callbacks to `POST /api/events/acs/callbacks`.
4. On `CallConnected`, API tries `AddParticipant` for the registered rep identity.
5. Rep accepts in-browser ACS incoming leg, creating the live customer <-> rep bridge.
6. WebSocket audio frames are parsed by `AcsAudioSource` and fed into the transcription pipeline.

Why this is the final demo fit:

- It is already implemented in API route wiring (`app.MapAcsRoutes()` + `app.MapRepRoutes()`).
- It preserves the explicit rep accept gate.
- It keeps the call path thin: one inbound webhook, one callback webhook, one media WebSocket.

## 2) Minimum endpoint set (with ownership)

| Endpoint | Method/Transport | Owner | Required for | Notes |
|---|---|---|---|---|
| `/api/events/acs/incoming-call` | `POST` webhook | API | Inbound call answer trigger | Must handle Event Grid subscription validation and IncomingCall events. |
| `/api/events/acs/callbacks` | `POST` webhook | API | Mid-call lifecycle events | Handles `CallConnected`/`AddParticipant*` outcomes. |
| `/api/calls/media-stream` | WebSocket (`wss`) | API | Live audio ingestion | Accepts ACS JSON media frames and forwards PCM to `AcsAudioSource`. |
| `/api/rep/token` | `GET` | API (+ Web proxy caller) | Rep softphone auth | Returns rep VoIP token when ACS identity service is configured. |
| `/api/rep/register` | `POST` | API (+ Web proxy caller) | Rep join path | Registers rep ACS user and triggers/reconverges AddParticipant. |
| `/api/calls/active` | `GET` | API | Late join/reconnect assist | Exposes active call id for browser state resync. |

## 3) Event Grid subscription and callback validation requirements

### Required Event Grid subscription

- Source: ACS resource events.
- Destination webhook: `https://<api-host>/api/events/acs/incoming-call`.
- Included event types:
  - `Microsoft.EventGrid.SubscriptionValidationEvent` (handshake)
  - `Microsoft.Communication.IncomingCall`

### Validation/auth expectations

- **Subscription validation is mandatory**: API must echo `data.validationCode` as `validationResponse` (implemented).
- **Delivery authentication must be enabled before live**: use Microsoft Entra-protected Event Grid delivery (or equivalent trusted delivery auth) prior to production/live-stage use.
- **Callback endpoints are anonymous at app auth layer by design** because ACS/Event Grid do not present API JWT bearer tokens.
- **Transport must stay HTTPS/WSS only** for inbound webhook and media-stream paths.

## 4) Media streaming format contract for Speech ingestion

Current ingestion assumptions (implemented):

- Streaming content: audio-only (`MediaStreamingContent.Audio`).
- Channel mode: **Unmixed** (`MediaStreamingAudioChannel.Unmixed`).
  - Rep frames (`participantRawId` prefix `8:`) are dropped to keep sentiment/transcript customer-focused.
  - If participant id is absent (mixed/fallback behavior), frame is accepted fail-open.
- Payload frames: ACS `AudioData` with base64 PCM payload.
- Target audio format into pipeline:
  - Encoding: `pcm16`
  - Sample rate: `16000` Hz
  - Channels: mono (`1`)
  - Typical packeting: 20 ms / ~640-byte payload at 16 kHz

## 5) Mission Control live-readiness reporting requirements

For ACS to be considered live-ready in Mission Control:

- `AcsMediaRoutesLiveReady = true`
- `acs-media-routes` component should move from `deferred/not-live-ready` to `live`.
- Evidence should confirm:
  - Event Grid subscription is active and validation passes.
  - Incoming-call and callbacks webhooks are reachable from Event Grid/ACS.
  - `wss://<api-host>/api/calls/media-stream` accepts ACS media connections.
  - A dress-rehearsal customer call reached transcription with expected audio format.

Until those checks pass, Mission Control must continue to surface mock/deferred state clearly.

## 6) Risks, operational constraints, and fallback boundary

### Risks / live-stage constraints

- Cold start risk can miss the inbound ring window.
- Event Grid delivery auth misconfiguration can silently block inbound-call events.
- WebSocket interruptions can terminate live transcription mid-call.
- ACS resource/identity/RBAC misconfiguration can break answer or AddParticipant.

### Demo fallback boundary (safe backup)

- If any live ACS readiness gate fails, set `AudioSource:Mode=Mock` on the API and `Frontend:LiveMode=false` on the Web app, then run the scripted feed path.
- Mock mode remains the approved reliability fallback, but live ACS is now the default interaction path.
