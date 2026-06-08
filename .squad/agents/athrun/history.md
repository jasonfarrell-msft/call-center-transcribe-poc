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
