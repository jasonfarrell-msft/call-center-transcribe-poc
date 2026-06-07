# Meyrin — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** ACS audio fork (Call Automation / media streaming), real-time WebSocket ingestion, backend APIs feeding the UI, and a swappable mock/scripted audio source for the demo.
- **Constraints:** POC may be scripted; must be able to run on mock audio. Managed identity, no secrets in code. Latest GA Azure SDKs.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(empty — append API contracts, ACS streaming notes, and key file paths here)

- **Team update:** POC plan drafted; shared WebSocket event contracts cover `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, and `next_best_action`; real-time loop now uses GPT-4o, with MAI-DS-R1 reserved for optional post-call analysis.
- **Team update (2026-06-05):** Stack is now C#/.NET + Razor + SignalR on ACA/App Service; `IReasoningClient` uses GPT-5.4 behind the interface; ACS follows Option A; shared events are C# DTOs with `transcript.detectedLanguage`.
- **2026-06-05T16:20:08.868-04:00 — Phase 0 scaffold completed:** Created `CallCenterTranscription.sln` with `Api`, `Web`, `Shared`, `Ai`, `Telephony`, and `Tests` projects targeting `net9.0`; wired project references (`Api -> Shared/Ai/Telephony`, `Web -> Shared`, `Tests -> Api/Shared/Ai/Telephony`) and validated with `dotnet build` + `dotnet test`.
- **2026-06-05T16:20:08.868-04:00 — Contract seam pattern:** Shared event DTOs now live under `src/CallCenterTranscription.Shared/Events/` with explicit JSON property names; `TranscriptEvent.DetectedLanguage` is pinned to `detectedLanguage` in the wire contract.
- **2026-06-05T16:20:08.868-04:00 — Mock-first backend seams:** `IAudioSource` (`src/CallCenterTranscription.Telephony/IAudioSource.cs`) and `IReasoningClient` (`src/CallCenterTranscription.Ai/IReasoningClient.cs`) are registered via `src/CallCenterTranscription.Api/ServiceCollectionExtensions.cs` to keep mock audio/reasoning swappable before real ACS/AI wiring.
- **2026-06-05T16:20:08.868-04:00 — Phase 0 security seam:** API scaffold adds optional auth gating with `Security:RequireAuth` in `src/CallCenterTranscription.Api/appsettings.json`, so Phase 1 can enable policy enforcement without redesigning routes/hub wiring.
- **2026-06-05T20:38:22Z — Scribe merge note:** Phase 0 decision inbox was merged into `.squad/decisions/decisions.md` and the inbox files were cleared.
- **2026-06-06T15:20:19.390-04:00 — Deployment artifact gap scan:** No `azure.yaml`, Bicep, or Dockerfiles exist yet; API already exposes `/healthz` and reads `Security:RequireAuth`, while Web currently requires `BackendApi:BaseUrl` and has no health endpoint. For the decided hosting split, API needs ACA container artifacts/probes and Web should use App Service source deploy unless the team later opts into a Web Docker path.
- **2026-06-06T15:20:19.390-04:00 — Auth seam caveat for deployment:** Enabling `Security:RequireAuth` without configuring a concrete authentication scheme (issuer/audience/token validation and Web→API credential flow) will break or leave routes unsecured; deployment planning must include explicit API/SignalR auth wiring decisions.
- **2026-06-06T15:29:41.673-04:00 — Deployment execution artifacts implemented:** Added Web `/healthz`, created root `azure.yaml` with `api` (`containerapp`) and `web` (`appservice`) services, and added API `.NET 9` Dockerfile exposing port `8080` with non-root runtime user (`USER $APP_UID`).
- **2026-06-06T15:29:41.673-04:00 — API container build context requirement:** Because API references sibling projects (`Shared`, `Ai`, `Telephony`), AZD `api.docker.context` must target repo root (`../..`) so Docker can copy all referenced projects during restore/publish.
- **2026-06-06T15:29:41.673-04:00 — Reverse-proxy safety for HTTPS redirects:** API and Web now enable forwarded headers (`X-Forwarded-For`/`X-Forwarded-Proto`) before `UseHttpsRedirection`, and exempt `/healthz` from redirect so platform probes return direct HTTP 200.
- **2026-06-06T15:29:41.673-04:00 — Container build hygiene:** Added repo-root `.dockerignore` to keep `bin/obj`, `.azure`, `.squad`, tests, and git metadata out of API Docker build context when AZD uses repo-root context.
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Reviewed the ingestion and streaming seams needed for transcript diarization, ad hoc translation, sentiment, and mission-control health.
- **2026-06-06T20:39:28.858-04:00 — Scripted frontend-support API contracts:** Expanded shared DTOs for transcript (`callId`, `eventId`, `utteranceId`, speaker label metadata, `source`), translation correlation (`relatedTranscriptEventId` + `utteranceId`), sentiment summaries, session metadata, and mission-control component health (`src/CallCenterTranscription.Shared/Events/*`).
- **2026-06-06T20:39:28.858-04:00 — Deterministic propane-retention mock feed:** Added `IScriptedScenarioFeed` + `ScriptedPropaneRetentionScenarioFeed` in API (`src/CallCenterTranscription.Api/ScriptedPropaneRetentionScenarioFeed.cs`) with fixed Maria Alvarez timeline (missed delivery, bill jump, competitor flyer Spanish segment, service-credit + budget-billing save) and wired routes `/api/session/current`, `/api/mission-control/health`, `/api/events/transcript|translation|sentiment`.
- **2026-06-06T20:39:28.858-04:00 — Mission Control readiness signaling:** Mission-control payload now explicitly flags mock feed active and ACS callback/media routes as deferred/not live-ready, while still reporting healthy live web/API/signalr surface.
- **2026-06-06T21:07:52.237-04:00 — Frontend/API disconnected-state hardening:** Web `Program.cs` no longer calls `UseHttpsRedirection`; backend edge handles HTTPS. `PipelineApiClient` now returns typed `ApiFetchResult<T>` with explicit failure kinds (configuration, connectivity, upstream, payload) instead of silent fallbacks.
- **2026-06-06T21:07:52.237-04:00 — UI error-state semantics:** `IndexModel` now records per-feed warnings and aggregate backend issues so Razor renders explicit disconnected/error messaging. Empty-state copy is now shown only when APIs returned successfully with empty data.
- **2026-06-06T21:07:52.237-04:00 — Regression coverage update:** Added tests for disconnected BaseUrl behavior, explicit API failure reporting from `PipelineApiClient`, homepage warning rendering, and `/healthz` no-redirect behavior in web host.
- **2026-06-06T21:07:52.237-04:00 — Safe warning surface + degraded summary:** UI warnings are now user-safe and generated from failure kind (details logged only). Connection summary now reports `Backend API degraded` when non-session feeds fail, with added tests for malformed payloads, partial-failure degradation, valid-empty payload semantics, and homepage non-redirect.
