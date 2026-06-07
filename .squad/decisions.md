# Squad Decisions

## Active Decisions

- **2026-06-05 | Squad** — Keep both diarization and translation: Azure AI Speech handles real-time STT/diarization, and Azure Translator handles non-English text so the POC supports both live attribution and rep comprehension.
- **2026-06-05 | Squad** — Translation is split by consumer: backend always translates for AI; the rep UI uses `detectedLanguage` plus a click-to-translate affordance. `transcript.detectedLanguage` is now part of the shared schema.
- **2026-06-05 | Lacus** — Translation trigger follows `transcript.detectedLanguage`; the backend may translate immediately or defer UI reveal, while still normalizing non-English text for churn/NBA/RAG.
- **2026-06-05 | Lunamaria** — Frontend recommendation was auto-translate inline with a language badge and original-text toggle. **Superseded** by the accepted click-to-translate rep-display decision, but retained as the UI rationale that informed the final shape.
- **2026-06-05 | Kira** — Demo/domain remains a propane-retention scenario with one scripted call, tiny mock customer/knowledge data, and churn signals tied to real propane complaints and save offers.
- **2026-06-05 | Squad** — Core platform is C#/.NET + Razor Pages/Blazor-capable UI + SignalR, with backend on Azure Container Apps and frontend on Azure App Service; GPT-5.x reasoning flows through `IReasoningClient`, with MAI swap-ready later.
- **2026-06-05 | Athrun** — Revised solution structure splits Api, Web, Shared, Ai, and Telephony projects, with `IAudioSource` / `IReasoningClient` abstractions and Bicep + CI scaffold. **Supersedes** the earlier TypeScript/Node/React architecture proposal.
- **2026-06-05 | Squad** — Real ACS is part of the final demo, with public callback/WebSocket endpoints on ACA and a fallback `MockAudioSource` for reliability.
- **2026-06-05 | Squad** — ACS call topology is Option A: customer dials the ACS number, backend answers, starts media streaming, then adds the rep via `AddParticipant`; mixed audio is the POC starting point, with a Phase-2 spike to validate rep audio after join.
- **2026-06-05 | Dyakka** — ACS dual-call/runbook work owns inbound answering, media streaming, rep join mechanics, and the repeatable demo script; Dyakka was hired mid-session to solve the two-party ACS path.
- **2026-06-05 | Squad** — Two review passes ran this session and both returned APPROVE-WITH-CHANGES; the canonical plan was updated with the required fixes before archive merge.

- **2026-06-05T16:20:08.868-04:00 — Phase 0 .NET scaffold baseline and seams**
- **By:** Meyrin
- **What:** Implemented the Phase 0 baseline as a `net9.0` multi-project solution (`Api`, `Web`, `Shared`, `Ai`, `Telephony`, `Tests`) with swappable interfaces (`IAudioSource`, `IReasoningClient`), shared real-time event DTO contracts (including `transcript.detectedLanguage`), SignalR-ready API routing, and mock-first DI registrations. Added an optional API auth seam (`Security:RequireAuth`) to enable route/hub authorization in later phases without changing contracts.
- **Why:** This keeps the demo seam clean between scripted mock audio and real ACS integration, preserves stream-first contract shape for UI/AI consumers, and de-risks Phase 1 by front-loading compile-safe wiring and contract tests while avoiding secrets and hardcoded environment credentials.
- **Source:** `.squad/decisions/inbox/meyrin-phase-0-scaffold.md`

- **2026-06-05T16:20:08.868-04:00 — Phase 0 reviewer gate verdict**
- **By:** Yzak
- **What:** Approved Meyrin's Phase 0 `.NET` scaffold after QA gate validation against the accepted architecture and acceptance criteria (solution shape, project references, core interfaces/contracts, `transcript.detectedLanguage`, SignalR-ready API/Web startup, and no hardcoded secrets/connection strings).
- **Why:** Live-demo reliability depends on compile-safe seams and predictable startup wiring before Phase 1+ integration. Validation evidence includes successful `dotnet build CallCenterTranscription.sln` and `dotnet test CallCenterTranscription.sln` (4/4 passing), so this scaffold is safe to advance.
- **Source:** `.squad/decisions/inbox/yzak-phase-0-review.md`

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
