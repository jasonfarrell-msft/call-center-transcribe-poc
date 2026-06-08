# Meyrin — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** ACS audio fork (Call Automation / media streaming), real-time WebSocket ingestion, backend APIs feeding the UI, and a swappable mock/scripted audio source for the demo.
- **Constraints:** POC may be scripted; must be able to run on mock audio. Managed identity, no secrets in code. Latest GA Azure SDKs.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(empty — append API contracts, ACS streaming notes, and key file paths here)

- **2026-06-08T12:05:43.410-04:00 — GitHub Actions Node20 → Node24 bump:**
  - **Workflows touched:** `deploy-frontend.yml` (all 5 flagged actions), plus `squad-heartbeat.yml`, `squad-issue-assign.yml`, `squad-triage.yml`, `sync-squad-labels.yml` (checkout only).
  - **Rationale:** GitHub enforces Node.js 24 on 2026-06-16 and removes Node.js 20 from runners on 2026-09-16. All five flagged actions still declared `node20` at their previously-pinned majors.
  - **Version targets and new pinned SHAs:**
    - `actions/checkout` → **v5** @ `93cb6efe18208431cddfb8368fd83d5badbf9bfd`
    - `actions/setup-dotnet` → **v5** @ `9a946fdbd5fb07b82b2f5a4466058b876ab72bb2`
    - `actions/upload-artifact` → **v7** @ `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` (v5 and v6 still node20)
    - `actions/download-artifact` → **v8** @ `3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c` (v5, v6, v7 still node20)
    - `azure/login` → **v3** @ `532459ea530d8321f2fb9bb10d1e0bcf23869a43` (v2.x still node20; v3.0.0 GA'd 2026-03-17)
  - **SHA-pinning convention:** Each `uses:` line pins to a full 40-char commit SHA. For annotated tags (where `git/refs/tags/vX` returns a tag-object SHA), deref via `GET /repos/{owner}/{repo}/git/tags/{tagObjectSha}` to reach the underlying commit SHA. Append a `# actions/name@vX` comment. Never use floating tags — tag-move supply-chain attacks are a real threat.
  - **Squad workflows:** previously used floating `actions/checkout@v4` (no SHA). Converted to SHA-pinned v5 as part of this bump; aligns with the project security-first policy.
  - **Residual risk:** `azure/webapps-deploy` (currently `b686016b` / v3) and `actions/github-script@v7` were out of scope but may also need attention before the Node20 removal deadline.

- **Team update:** POC plan drafted; shared WebSocket event contracts cover `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, and `next_best_action`; real-time loop now uses GPT-4o, with MAI-DS-R1 reserved for optional post-call analysis.
- **Team update (2026-06-05):** Stack is now C#/.NET + Razor + SignalR on ACA/App Service; `IReasoningClient` uses GPT-5.4 behind the interface; ACS follows Option A; shared events are C# DTOs with `transcript.detectedLanguage`.
- **2026-06-05T16:20:08.868-04:00 — Phase 0 scaffold completed:** Created `CallCenterTranscription.sln` with `Api`, `Web`, `Shared`, `Ai`, `Telephony`, and `Tests` projects targeting `net9.0`; wired project references (`Api -> Shared/Ai/Telephony`, `Web -> Shared`, `Tests -> Api/Shared/Ai/Telephony`) and validated with `dotnet build` + `dotnet test`.
- **2026-06-05T16:20:08.868-04:00 — Contract seam pattern:** Shared event DTOs now live under `src/CallCenterTranscription.Shared/Events/` with explicit JSON property names; `TranscriptEvent.DetectedLanguage` is pinned to `detectedLanguage` in the wire contract.
- **2026-06-05T16:20:08.868-04:00 — Mock-first backend seams:** `IAudioSource` (`src/CallCenterTranscription.Telephony/IAudioSource.cs`) and `IReasoningClient` (`src/CallCenterTranscription.Ai/IReasoningClient.cs`) are registered via `src/CallCenterTranscription.Api/ServiceCollectionExtensions.cs` to keep mock audio/reasoning swappable before real ACS/AI wiring.
- **2026-06-05T16:20:08.868-04:00 — Phase 0 security seam:** API scaffold adds optional auth gating with `Security:RequireAuth` in `src/CallCenterTranscription.Api/appsettings.json`, so Phase 1 can enable policy enforcement without redesigning routes/hub wiring.
- **2026-06-05T20:38:22Z — Scribe merge note:** Phase 0 decision inbox was merged into `.squad/decisions/decisions.md` and the inbox files were cleared.
- **2026-06-08T13:48:02Z — Dashboard contrast fix:** Fixed WCAG AA color contrast failure in Lunamaria's dashboard redesign by swapping `--cc-text-muted` (#94a3b8, 2.36–2.56:1) → `--cc-text-secondary` (#475569, 7.58:1) on 8 light-card CSS rules (`.transcript-speaker-block p`, `.transcript-topline time`, `.sentiment-score-label`, `.sentiment-meter-caption`, `.translation-label`, `.panel-kicker`, `.sentiment-details dt`, `.mission-control-summary span`). Dark-header rules (`.console-eyebrow`, `.console-call-meta dt`, `.console-status`) unchanged and compliant. Build: 0 errors, 0 warnings. Approved by Athrun re-gate.
- **2026-06-08T12:58:45Z — Mission Control separate-page regression fix:** Lunamaria's nav-toggle removal accidentally deleted the `const translationButton` declaration in site.js click handler, causing ReferenceError on every translation toggle click. Fixed by restoring the missing const line and realigning `case "transcript-scroller":` indentation in `restoreFocus`. Both `node --check` and `dotnet build` pass clean. Approved on Athrun re-gate.
