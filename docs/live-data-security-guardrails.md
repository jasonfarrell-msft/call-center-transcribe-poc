# Live Data Security Guardrails (Issue 030)

This document defines the minimum security/privacy posture before exposing live transcript data.

## 1) Web -> API -> SignalR auth model (implementation-ready)

- **Identity provider:** Microsoft Entra ID (OIDC/OAuth2).
- **Web app sign-in:** interactive user sign-in against Entra ID.
- **API access:** Web requests an access token for the API audience (`Security:Auth:Audience`) and sends it as `Authorization: Bearer`.
- **SignalR access:** the same bearer token is supplied to `/hubs/pipeline/negotiate` and hub connections (`access_token` query for WebSocket/SSE fallback).
- **API auth switch:** `Security:RequireAuth=true` enables policy enforcement on REST + hub endpoints.
- **Startup guardrail:** when `Security:RequireAuth=true`, `Security:Auth:Authority` and `Security:Auth:Audience` are required; startup fails if missing.

## 2) Authorization boundaries

- **Policy:** `AgentAssistAccess` requires an authenticated user for:
  - `/api/session/current`
  - `/api/mission-control/health`
  - `/api/events/*`
  - `/hubs/pipeline` (including `/hubs/pipeline/negotiate`)
- **Per-session/per-call rules for live mode (to implement with real identity claims):**
  - Require a call/session access claim (for example `call.read:{callId}` or `session.read:{sessionId}`).
  - Reject calls/hub group joins where claim scope does not match requested call/session.
  - Hub group names must be deterministic and scoped (`call:{callId}`), never user-provided raw group IDs.

## 3) CORS/origin rules

- **Production:** allow only the deployed Web app origin(s); no wildcard origins.
- **Local development:** explicitly allow local HTTPS origins used by the web app tooling (for example `https://localhost:*` values used by local profiles).
- **SignalR:** require credentialed requests only from allowed origins; block cross-origin negotiate requests from unknown domains.

## 4) Telemetry/logging redaction rules

- Do **not** log raw transcript text, translated text, customer names, phone numbers, account numbers, or free-form notes by default.
- Log structured metadata only (event id, call id/session id, speaker role, language code, latency, status).
- If transcript excerpts are required for troubleshooting, gate behind an explicit temporary debug flag and redact PII fields before emit.
- Keep App Insights retention and export rules aligned with least-privilege and privacy review outcomes.

## 5) ACS/Event Grid callback validation requirements

- Validate Event Grid subscription handshake (`aeg-event-type: SubscriptionValidation`) before accepting events.
- Validate Event Grid delivery signatures/headers for each event request.
- Validate ACS callback payload schema and expected resource identifiers.
- Enforce HTTPS-only callbacks and reject unsigned/untrusted sources.
- Keep callback endpoints idempotent and replay-safe.

## 6) Mock/live safety switch

- `AudioSource:Mode` is the customer↔rep interaction mode switch.
  - `Acs` (default): the app stays on the live interaction path and waits for a real call.
  - `Mock`: explicit deterministic fallback for scripted demo playback.
- `Frontend:LiveMode` is the web-console rendering switch.
  - `true` (default): live header/softphone/SignalR experience for ACS calls.
  - `false`: scripted/polling console mode that intentionally surfaces mock customer/session metadata.
- When using the scripted interaction fallback, set `AudioSource:Mode=Mock` **and** `Frontend:LiveMode=false` together so API and web remain coherent.
- UI/API surfaces must continue exposing `IsMockFeedActive` so operators can see when mock data is active.

## 7) Unauthorized access test checklist

- [x] REST unauthorized test: `/api/session/current` returns `401` when `Security:RequireAuth=true` and no token is provided.
- [x] SignalR unauthorized test: `/hubs/pipeline/negotiate` returns `401` when `Security:RequireAuth=true` and no token is provided.
- [x] Config guard test: app fails startup if `Security:RequireAuth=true` without `Security:Auth:Authority` and `Security:Auth:Audience`.
- [x] Mock fallback remains opt-in via `AudioSource:Mode=Mock`; live mode is the default startup path.
