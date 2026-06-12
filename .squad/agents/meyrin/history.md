# Meyrin — History [SUMMARY]

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for propane call center
- **Focus:** ACS audio fork (Call Automation / media streaming), WebSocket ingestion, backend APIs, swappable mock/scripted audio
- **Constraints:** Managed identity, no secrets in code, latest GA Azure SDKs
- **Created:** 2026-06-05

## Key Decisions & Work Completed

### Phase 0 Scaffold (2026-06-05)
- Created `CallCenterTranscription.sln` with Api, Web, Shared, Ai, Telephony, Tests projects (net9.0)
- Wired project references validated with `dotnet build` + `dotnet test`
- Shared event DTOs under `src/CallCenterTranscription.Shared/Events/`
- Seams: `IAudioSource` (mock/ACS swappable), `IReasoningClient` (reasoning client abstraction)
- Security seam: optional auth gating via `Security:RequireAuth`

### Infrastructure & Deployment (2026-06-08–2026-06-10)

**ACS Go-Live Infrastructure:**
- Event Grid System Topic + Subscription in Bicep (system topic type for ACS, webhook delivery)
- ACS RBAC: "Communication Services Owner" (GUID `09976791-48a7-449e-bb21-39d1a415f350`) scoped to ACA system MI
- `minReplicas` param: switched from hardcoded 0 → parameterized default 1
- `AudioSource__Mode` env var: added `'Mock'` default, flip to `'Acs'` after Event Grid wired
- Speech RBAC verified live: `Cognitive Services User` already assigned to ACA MI on Speech resource
- Deploy recipe: `az acr build` (ACR Tasks) → `az containerapp update` (no `azd` workflow needed)

**GitHub Actions Node20 → Node24 Bump (2026-06-08):**
- Pinned all flagged actions to node24 versions with full commit SHAs
- `checkout@v5`, `setup-dotnet@v5`, `upload-artifact@v7`, `download-artifact@v8`, `azure/login@v3`
- Squad workflows: converted floating `@v4` checkout to SHA-pinned v5

**Bug Fixes:**
- WCAG AA contrast: swapped `--cc-text-muted` → `--cc-text-secondary` on 8 light-card CSS rules (7.58:1)
- Lunamaria nav-toggle fix: restored missing `const translationButton` in site.js
- ACS RBAC GUID fix: `2b4609a5-7812-4aba-b5e3-076e6a078419` (unavailable) → `09976791-48a7-449e-bb21-39d1a415f350` (available in directory)
- ACS dataLocation: EU → US (immutable field, required manual resource deletion + reprovision)

### Static Assets & Caching (2026-06-08)

**LibMan Integration:**
- `libman.json` at `src/CallCenterTranscription.Web/`
- Provider: jsdelivr; pinned versions: bootstrap@5.3.3, jquery@3.7.1, jquery-validation@1.21.0, jquery-validation-unobtrusive@4.0.0
- Deploy workflow: `dotnet tool restore && dotnet tool run libman restore` before `dotnet publish`

**HTML No-Cache Middleware:**
- Added `app.Use` middleware (Program.cs) fires `Cache-Control: no-cache, no-store, must-revalidate` only for text/html
- Static assets (CSS, JS, fonts) retain fingerprint-based `max-age, immutable` caching
- Health checks and non-HTML endpoints unaffected

### Rep Call-Control Backend Integration (2026-06-10)

**Task 3 — `repAccepted` event broadcast:**
- In `HandleCallbacksAsync`, on `AddParticipantSucceeded`, broadcast `repAccepted` SignalR event
- Differentiates "Call Pending" (ringing) from "Connected" (rep accepted, transcript visible)
- Merge order: Meyrin (Task 3, additive) **first** → Dyakka rebases (Task 4 on same file)

## Production Readiness

- All Bicep validated: 0 errors, 0 warnings
- Deploy recipe de-risks `azd provision` by using surgical `az` commands post-image-deploy
- Event Grid subscription creation must follow API webhook deployment (handshake requirement)
- Speech RBAC verified live; ACS RBAC GUIDs confirmed directory-available

## Learnings

- README review fix: architecture diagrams must show the rep/browser call leg terminating at ACS, while API/Web edges stay labeled as control-plane flows; telemetry caveats should describe required hardening targets, not imply current enforcement.
- The current backend already streams and replays `KnowledgeCardEvent` + `NextBestActionEvent` keyed to transcript turns; the missing handoff is not a new retrieval service but an utterance-correlated assist contract for the live rep experience. For POC shape, assist should fire on customer turns only, after translation when needed, and remain compatible with both scripted feed and ACS live audio.
- 2026-06-11T16:42:31.815-04:00 — Rep accept latency can be reduced by moving `AddParticipant` earlier: right after `AnswerCallAsync` succeeds, emit `stream.callPending` first, then immediately attempt `AddParticipant` when a rep is already registered (with `CallConnected` still acting as fallback reconverge). Guard `AddParticipantSucceeded/Failed` callbacks against stale call IDs so `RepAccepted` and add-state never bleed across call lifecycles.
- 2026-06-11T16:49:37.710-04:00 — Added a deterministic demo-assist slice for the three scripted conversations: JSONL knowledge + trigger expectations are now loaded in-process, customer-only utterances emit evidence-backed `KnowledgeCardEvent` cards (citation/source section/rank/matchedEvidence) plus `NextBestActionEvent`, and the scripted feed can switch scenarios via `DemoScript__ScriptId` while reusing the same assist contract the live SignalR stream already understands.
- 2026-06-12T10:14:21.594-04:00 — Early Accept state now needs one authoritative backend source for both REST bootstrap and SignalR replay. Seeding live ACS mode with an idle snapshot, flipping to `pending` immediately after `AnswerCallAsync`, replaying lifecycle events before any transcript, and suppressing transcript replay while pending gives the web tier a clean pre-speech Accept contract without leaking scripted history into live mode.
- 2026-06-12T13:16:06.171-04:00 — Exposing live Accept too early created a synthetic pending state: the safe backend contract is to answer/store the PSTN call first, but publish `pending` only after ACS `CallConnected` and a successful `AddParticipant` invite to the rep leg. Mock/scripted mode must never surface ACS acceptability, and all connected/accepted/teardown transitions need call-id guards so stale callbacks from the prior call cannot mutate the next live call.
