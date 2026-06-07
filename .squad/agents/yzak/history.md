# Yzak — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** Test cases + demo-script validation + edge cases (non-English, overlapping speakers, churn escalation, empty knowledge hits). Reviewer gate.
- **Priority:** The scripted demo must survive a live audience, every run.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(empty — append test scenarios, known fragile spots, and validation results here)

- **Team update:** POC plan drafted; shared WebSocket event contracts cover `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, and `next_best_action`; real-time loop now uses GPT-4o, with MAI-DS-R1 reserved for optional post-call analysis.
- **Team update (2026-06-05):** Tests now target the C#/.NET + Razor + SignalR stack on ACA/App Service, real ACS Option A, and shared events that include `transcript.detectedLanguage`.
- **2026-06-05T16:20:08.868-04:00 — Reviewer gate result: APPROVED.** Verified the Phase 0 scaffold includes Api/Web/Shared/Ai/Telephony/Tests in `CallCenterTranscription.sln`, project references are sane, `IAudioSource` + `IReasoningClient` + shared events compile cleanly, transcript JSON contract includes `detectedLanguage`, API/Web startup is compile-safe with SignalR hub wiring, no hardcoded secrets/connection strings were introduced, and `dotnet build CallCenterTranscription.sln` + `dotnet test CallCenterTranscription.sln` both passed (4/4 tests).
- **2026-06-05T20:38:22Z — Scribe merge note:** Phase 0 review was archived into `.squad/decisions/decisions.md` and the inbox file was cleared.
- **2026-06-06T15:20:19.350-04:00 — Azure deployment readiness review:** Rejected the current deployment plan as not ready for team sign-off. The chosen Azure direction still matches Squad decisions, but `.azure/deployment-plan.md` is only a skeleton and does not define managed-identity/RBAC boundaries, Key Vault vs secret handling, production auth posture, frontend/API health probes, ACA public callback/WebSocket validation, demo fallback/runbook gates, or post-deploy smoke checks. Evidence baseline remained clean: `dotnet build CallCenterTranscription.sln` and `dotnet test CallCenterTranscription.sln --no-build` both passed (4/4).
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Captured the validation posture for transcript diarization, ad hoc translation, sentiment, and mission-control health.
