# Athrun — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **Goal:** Demonstrable web app. Fork live call audio via Azure Communication Services, run a pipeline (diarization → translation → continual sentiment → churn-risk agent → knowledge surfacing → next-step suggestions), render to an agent-assist UI.
- **Industry:** Propane retail. Churn = customer decides to stop buying propane from this company.
- **Constraints:** POC may be scripted; data/knowledge mocked to simplest case. Mid-tier models, **MAI preferred**, via latest GA Azure AI Foundry. Security: managed identity, no secrets in code.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

### 2026-06-05 — Initial Architecture Decisions (SUPERSEDED)

1. ~~**Stack:** TypeScript end-to-end (Node 22 + React 19/Vite).~~ → SUPERSEDED by .NET/Razor pivot.
2. **Audio-source abstraction:** `AudioSourceProvider` concept CARRIED FORWARD as `IAudioSource` (.NET interface).
3. ~~**AI platform:** MAI-DS-R1 for churn/NBA/RAG.~~ → SUPERSEDED: GPT-5.x (5.4 target) via Azure AI Foundry; MAI deferred until GA.
4. **Auth pattern:** `DefaultAzureCredential` everywhere. No secrets in code. → STILL VALID.
5. ~~**Hosting:** Single ACA container.~~ → SUPERSEDED: split hosting (ACA backend + App Service frontend).
6. **Phase strategy:** 4 phases concept CARRIED FORWARD with modifications (see revised plan).
7. ~~**Key file:** `.squad/decisions/inbox/athrun-poc-architecture.md`~~ → SUPERSEDED by `.squad/decisions/inbox/athrun-dotnet-architecture.md`.

### 2026-06-05 — Revised Architecture: C#/.NET + Razor Pivot

**Trigger:** Owner decision (`squad-stack-and-platform.md`) mandated C#/.NET + Razor + SignalR, split hosting (ACA + App Service), GPT-5.x model, and real ACS as in-scope (not stretch).

**Key changes from original:**
1. **Stack → C#/.NET 9 (latest LTS), ASP.NET Core, Razor Pages (Blazor-capable), SignalR for real-time push.** Replaces Node/React/socket.io.
2. **Model → GPT-5.4 via Azure.AI.OpenAI / AI Foundry.** Behind `IReasoningClient` interface so MAI can replace it via config when GA. Project policy: GPT 5.3+ only.
3. **Hosting split:** Backend on Azure Container Apps (WebSocket ingress for ACS media streaming + SignalR hub). Frontend Razor on Azure App Service. Both use Managed Identity.
4. **Real ACS now in-scope.** Dyakka owns `AcsAudioSource` + dual-call script. `IAudioSource` abstraction preserves mock-first dev.
5. **Solution layout:** 5 projects — `Api`, `Web`, `Shared`, `Ai`, `Telephony` — plus `Tests`.
6. **Phases resequenced:** Phase 0 (.NET scaffold) → Phase 1 (end-to-end mock audio, Razor UI, SignalR, GPT-5.x) → Phase 2 (real ACS dual-call) → Phase 3 (polish).
7. **Key file:** `.squad/decisions/inbox/athrun-dotnet-architecture.md`.

### 2026-06-06 — Sweden Central deployment architecture

1. **Minimal Azure inventory:** ACR + ACA environment/app for the API, App Service Plan + Web App for the frontend, Azure AI Speech, Azure AI Language, Azure AI Foundry regional deployments, Key Vault, and shared Log Analytics/Application Insights in `swedencentral`, plus ACS + phone number + Event Grid for the live call path.
2. **Named regional blockers:** strict Sweden Central processing is incompatible with two accepted POC elements as currently defined — **ACS** is geography/global-event based, and **Translator Text** Europe processing runs in **France Central / West Europe** rather than Sweden Central.
3. **Demo reliability posture:** keep the ACA backend warm on a single replica during rehearsals/demo until the real ACS callback/media path is proven replica-safe; public HTTPS/WSS ingress is required for callbacks and media streaming.
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Participated in the cross-agent planning batch for transcript diarization, ad hoc translation, sentiment, and mission-control health.

### 2026-06-08 — Dashboard visual redesign review (Lunamaria)

Reviewed Lunamaria's token-based CSS rewrite + `data-speaker-role` addition in Index.cshtml.

**Lessons learned:**
1. **Design tokens that span dark + light surfaces need two separate "muted" values.** `--cc-text-muted: #94a3b8` reads well on the dark navy header (4.63–5.92:1 against gradient) but fails WCAG AA on all white/near-white card backgrounds (2.36–2.56:1, needs 4.5:1). A single muted token cannot serve both dark and light contexts simultaneously. Solution: use `--cc-text-secondary` (#475569, 7.58:1) for light-surface secondary labels, reserve the light muted value for the dark header.
2. **WCAG AA is non-negotiable even for metadata/supplementary text.** Role labels, timestamps, score labels, and DT captions are not exempt. If it's visible text, it passes or we fix it.
3. **Otherwise the redesign approach was sound:** speaker-role attribution via `data-speaker-role` is clean, JS selectors all intact, no content removed, no CDN additions, `prefers-reduced-motion` honored, color never sole status indicator.
4. **Gate rule exercised:** Revision assigned to Meyrin (not original author Lunamaria) per the reviewer gate policy.

### 2026-06-08 — Mission Control screen-split review (Lunamaria)

APPROVED. Two full-viewport screens with persistent nav bar. Flex layout fills remaining height correctly (`.rep-console` column flex, `.console-view` flex:1, `.screen-nav` flex-shrink:0). JS `setActiveView` correctly manages `hidden`+`aria-hidden` and `aria-current` on nav buttons. Selectors all resolve. WCAG AA contrast passes for nav text (`#e8f0fd` on `#0c1e4a`). No content removed, no secrets, no new external assets.

### 2026-06-08 — Console 80/20 column layout review (Lunamaria)

APPROVED. `grid-template-columns: 4fr 1fr` delivers the 80/20 split Jason asked for. Mission Control restyled from pill to dimmed text link with underline hover + border-bottom active. Height chain verified end-to-end (100dvh → flex → flex:1 grid → transcript-scroller overflow-y:auto). Body overflow:hidden prevents page scroll. JS untouched, all selectors resolve. WCAG AA contrast confirmed (≈5.88:1 for muted nav text). No content removed, no secrets.

### 2026-06-08 — GitHub Actions Node20→Node24 bump review (Meyrin)

APPROVED. Meyrin bumped 5 actions (checkout v5, setup-dotnet v5, upload-artifact v7, download-artifact v8, azure/login v3) across all 5 workflows to Node24-compatible releases with full 40-char SHA pins. All SHAs independently verified against upstream repos. No logic drift — only `uses:` lines changed. Non-blocking follow-up: `actions/github-script@v7` remains floating-tagged in 6 squad-workflow locations (pre-existing, not in scope of this change).

### 2026-06-08 — Mission Control separate-page review (Lunamaria)

REQUEST CHANGES. Architecture is sound (real Razor Page, tag helpers resolve, content correctly moved, Index console-only, build clean). **Blocker:** site.js click handler references `translationButton` without declaring it — the `const translationButton = target.closest(...)` line was accidentally deleted during nav-toggle removal. Throws ReferenceError on every click, breaking translation toggles. Minor: switch-case indentation misaligned. Fix assigned to Meyrin per reviewer gate policy.

### 2026-06-08 — Mission Control separate-page RE-GATE (Meyrin regression fix)

APPROVED. Meyrin fixed the `translationButton` ReferenceError (restored missing const declaration) and realigned `case "transcript-scroller":` indentation in `restoreFocus`. Both fixes verified; `node --check` clean, build 0 errors. No regressions. Gate APPROVE.

### 2026-06-08 — /lib asset provisioning + HTML no-cache middleware review (Meyrin)

APPROVED. Reviewed libman.json (bootstrap 5.3.3, jQuery 3.7.1, jquery-validation 1.21.0, jquery-validation-unobtrusive 4.0.0) — destination paths align exactly with `_Layout.cshtml` and `_ValidationScriptsPartial.cshtml` `~/lib/…` references. dotnet-tools.json pins libman CLI to 3.0.71. Workflow step is a plain `run:` in the build job between restore and publish — correct ordering, no new action, SHA pins unchanged. Cache middleware uses `OnStarting` with `Content-Type.StartsWith("text/html")` guard — static assets, /healthz, and APIs unaffected. Middleware order preserved. Build 0 errors; publish output confirmed all 5 assets at real sizes (87–232 KB). Supply chain acceptable: versions pinned exact, provider is well-known jsdelivr CDN.

### 2026-06-08 — ACS Option C Architecture & Security Sign-Off

APPROVE TO BUILD. Signed off Dyakka's ACS assessment + Jason's Option C selection with binding implementation decisions:

1. **RBAC role:** `Communication Services Contributor` (`2b4609a5-7812-4aba-b5e3-076e6a078419`) scoped to the ACS resource only. No narrower built-in role exists for Call Automation + media streaming. Residual risk documented (management-plane access); mitigated by resource-level scope + system-assigned identity isolation.
2. **Webhook auth:** SubscriptionValidationEvent handshake + schema validation only this round. Entra-protected delivery auth deferred to Event Grid wiring round (next). No HMAC, no secrets.
3. **WS topology / minReplicas:** New Bicep param `apiMinReplicas` default 1. maxReplicas stays 1 (already set). Single replica = no affinity needed. Dropped WebSocket = end-of-stream (no reconnect in POC).
4. **DI seam:** `AudioSource:Mode` config key (`"Mock"` default | `"Acs"`). Env var swap, no rebuild. MockAudioSource remains default.
5. **Scope IN:** AcsAudioSource, IncomingCall webhook, media-stream WS, DI swap, Bicep RBAC + minReplicas + env var. **OUT:** PSTN number, Event Grid subscription, Entra webhook auth, audio→Speech consumer (immediate NEXT round, not this one).
6. **SDK:** `Azure.Communication.CallAutomation` (GA), `DefaultAzureCredential` auth. Zero connection strings.

Key file: `.squad/decisions/inbox/athrun-acs-option-c-signoff.md`

### 2026-06-08 — ACS RBAC Role Correction (Self-Revision)

**Trigger:** Deploy-time failure — `az role assignment create` with GUID `2b4609a5-7812-4aba-b5e3-076e6a078419` ("Communication Services Contributor") returns `RoleDefinitionDoesNotExist`. The role does not exist in this directory/subscription.

**Findings:**
1. `az role definition list --query "[?contains(roleName, 'Communication')]"` returns exactly ONE built-in role: **"Communication and Email Service Owner"** (`09976791-48a7-449e-bb21-39d1a415f350`).
2. That role grants management-plane actions on Communication and Email services (read/write/delete/ListKeys/RegenerateKey/EventGridFilters) but has an empty `dataActions` array. It is the only available built-in role for ACS in this directory.
3. The Bicep at `infra/main.bicep` line 86–88 hardcodes the non-existent GUID — a latent defect that would cause `azd provision` to fail on any fresh deployment.

**Corrected decision:** Use `09976791-48a7-449e-bb21-39d1a415f350` ("Communication and Email Service Owner") in place of the non-existent `2b4609a5-...`. Same scope (ACS resource only) and same principal (ACA system MI). Residual risk is slightly broader (includes Email Service management actions and ListKeys/RegenerateKey) but acceptable for POC: resource-scoped, system-assigned MI only, no external exposure, AudioSource__Mode is currently Mock.

**Lesson:** Always verify role definition GUIDs against the TARGET subscription/directory before committing to IaC. Built-in role catalogs vary by subscription type and region availability. "Communication Services Contributor" does not exist as a built-in role (it may be documentation-only or preview-removed).

## 2026-06-08 — RBAC Decision Revision
**Status:** COMPLETED & DOCUMENTED

Discovered that the original RBAC role choice ("Communication Services Contributor" GUID 2b4609a5-7812-4aba-b5e3-076e6a078419) does not exist in this Azure directory. Revised decision:

- **Corrected Role:** Communication and Email Service Owner (09976791-48a7-449e-bb21-39d1a415f350)
- **Rationale:** Only available built-in ACS role; broader than ideal but POC-acceptable
- **Scope:** Resource-scoped to ACS; applied to ACA system MI
- **Sign-off:** Supersedes Option C role decision

Decision documented in:
- decisions.md (merged from inbox/athrun-acs-rbac-correction.md)
- orchestration-log/20260608T190537Z-athrun-rbac-correction.md

Next phase: Event Grid + audio consumer (Lacus + Meyrin).

### 2026-06-08 — ACS Go-Live Sign-Off (Event Grid + Consumer + Speech)

**Trigger:** Real US toll-free +18774178275 purchased on ACS. Gaps: no Event Grid subscription, no audio→Speech consumer, minReplicas=0, Mode still Mock on live app.

**Decisions made:**

1. **Event Grid delivery auth: Plain webhook (SubscriptionValidationEvent handshake only).** Entra-protected delivery deferred. Residual risk assessed: forged IncomingCall POST → AnswerCall against invalid context → ACS rejects (4xx). No data exfil, no cost, no state corruption. Acceptable for POC.

2. **Live provisioning mechanism: Surgical `az containerapp update`** (not `azd provision`). The azd env is bare; full provision risks drift. Consistent with prior ACS recreate approach. Intended end-state: minReplicas=1, AudioSource__Mode=Acs, applied atomically as the LAST step after consumer + Event Grid are ready.

3. **Audio→Speech consumer shape: `SpeechTranscriptionService : BackgroundService`** in the Api project.
   - Resolves `IAudioSource`, calls `ReadAsync()` to pull PCM frames
   - Feeds `Microsoft.CognitiveServices.Speech` SDK (PushAudioInputStream, continuous recognition)
   - Language auto-detect from existing `Speech:CandidateLanguages` config
   - Emits interim (`Recognizing`, isFinal=false) and final (`Recognized`, isFinal=true) `TranscriptEvent`s via `IHubContext<PipelineHub>` to group `call:{callId}` on method `stream.transcript`
   - UI already subscribes to this SignalR stream — no frontend changes needed
   - Coexists with scripted feed (different path: REST vs SignalR)
   - `DemoSafety:DataMode` startup guard must be removed/relaxed

4. **Speech resource + RBAC: Already provisioned.**
   - Resource: `speech-cctrans-{suffix}` (SpeechServices, swedencentral, S0)
   - Role: `Cognitive Services User` (`a97b65f3-24c7-4388-baec-2e87135dc908`) — scoped to Speech resource, applied to ACA system MI
   - GUID is a global built-in (high confidence it exists), but Meyrin must still verify via `az role definition list` before relying on it (lesson from ACS RBAC burn)
   - Must verify the role assignment is actually LIVE on the running app (may have been lost during surgical ACS recreate)

5. **Go-live sequence:** Consumer built → Speech/ACS RBAC verified → new image deployed → Event Grid subscription created → surgical az flip (minReplicas=1 + Mode=Acs) → test call → transcript visible in UI. MockAudioSource remains the instant fallback (30-second revert via env var flip).

**Key file:** `.squad/decisions/inbox/athrun-acs-go-live-signoff.md`
