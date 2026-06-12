---
## Archived Active Decisions
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

## Archived Decision Records
