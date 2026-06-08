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
- **2026-06-08T13:48:02Z — Dashboard contrast fix:** Fixed WCAG AA color contrast failure in Lunamaria's dashboard redesign by swapping `--cc-text-muted` (#94a3b8, 2.36–2.56:1) → `--cc-text-secondary` (#475569, 7.58:1) on 8 light-card CSS rules (`.transcript-speaker-block p`, `.transcript-topline time`, `.sentiment-score-label`, `.sentiment-meter-caption`, `.translation-label`, `.panel-kicker`, `.sentiment-details dt`, `.mission-control-summary span`). Dark-header rules (`.console-eyebrow`, `.console-call-meta dt`, `.console-status`) unchanged and compliant. Build: 0 errors, 0 warnings. Approved by Athrun re-gate.
