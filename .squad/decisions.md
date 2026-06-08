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

- **Source:** `.squad/decisions/inbox/athrun-azure-deployment-architecture.md`

# 2026-06-06T15:20:19.326-04:00 — Minimal Azure deployment architecture for the Sweden Central POC

- **By:** Athrun
- **Decision proposal:** Keep the POC architecture as the thinnest viable split already accepted by Squad: **API on Azure Container Apps**, **Web on Azure App Service**, **real ACS for the live demo**, **Azure AI Speech + Translator + Language + Azure AI Foundry** for the AI path, and **mock audio** as the fallback path.

## Assumptions

1. The deployment target is **resource group `rg-callcentertranscribe-swc-mx01`**.
2. Regional Azure resources should use **`swedencentral`** unless the service itself only supports **geography/global semantics**.
3. If a required service cannot satisfy strict Sweden Central processing, that is a **named blocker/exception**, not something we silently route around.

## Must-have resources

1. **Azure Container Registry (Basic)** in `swedencentral`
   - Hosts the API container image for ACA.
2. **Azure Container Apps managed environment** in `swedencentral`
3. **One Azure Container App** for `CallCenterTranscription.Api`
   - Public HTTPS ingress enabled
   - WebSockets enabled for ACS media streaming and SignalR
   - System-assigned managed identity
   - **Demo posture:** `minReplicas=1`, `maxReplicas=1` until the real ACS path is proven multi-replica-safe
4. **One Linux App Service Plan** in `swedencentral`
   - Start at a small paid SKU suitable for one demo user path
5. **One Web App** for `CallCenterTranscription.Web`
   - System-assigned managed identity
6. **Azure AI Speech** in `swedencentral`
   - Real-time STT + diarization
7. **Azure AI Language** in `swedencentral`
   - Sentiment only; no custom authoring assumptions
8. **Azure AI Foundry** in `swedencentral`
   - One **regional standard** GPT-5.x reasoning deployment
   - One embeddings deployment if RAG embeddings stay externalized instead of purely local
   - Exact model/version/quota must be validated before deployment
9. **Azure Key Vault** in `swedencentral`
   - Required for any unavoidable secrets/certificates
   - If managed identity covers everything, it should stay nearly empty
10. **Log Analytics workspace** in `swedencentral`
11. **Application Insights** (workspace-based) in `swedencentral`
12. **Azure Communication Services resource**
   - Same resource group
   - Use the closest valid geography configuration for the service
13. **One ACS phone number asset**
   - Required for the inbound PSTN demo
14. **Event Grid subscription** from ACS `IncomingCall` to the ACA webhook
15. **Azure AI Translator**
   - Functionally required by the accepted translation decision
   - See blocker below: strict Sweden Central processing is not currently achievable with Translator Text

## Explicit blockers / region exceptions

1. **ACS is not a normal per-datacenter regional service**
   - The ACS resource is created against a **geography**, not a Sweden Central datacenter stamp.
   - Microsoft documents that ACS data **may transit or be processed in other geographies**.
   - ACS Event Grid **system topics are global** and may store event data in any Microsoft datacenter.
2. **Translator Text does not keep Europe requests inside Sweden Central**
   - Microsoft documents the Europe endpoint as processing within **France Central** and **West Europe**.
   - Translation is therefore a **functional must-have** that conflicts with a **strict Sweden Central processing** requirement.
3. **Azure AI Foundry must be pinned to an exact regional deployment**
   - “GPT-5.x” is too vague for deployment.
   - If the exact reasoning model or embedding model is not available as a **regional** deployment in `swedencentral`, treat that as a blocker instead of falling back to Data Zone or Global without approval.

## Phase-later / explicitly deferred

- Azure AI Search
- Redis / caching tier
- Cosmos DB / SQL DB / durable transcript persistence
- Blob storage beyond platform defaults
- Azure SignalR Service / Web PubSub
- VNet integration, private endpoints, WAF, Front Door
- Separate ACA services/jobs for pipeline subdivision
- Provisioned Foundry throughput
- Multi-region resiliency

## Security requirements

1. **Managed identity first**
   - ACA and App Service use system-assigned managed identity by default.
2. **No secrets in code or appsettings**
   - Any unavoidable key/certificate lives in Key Vault.
3. **Public ingress is allowed only where the demo requires it**
   - ACA exposes HTTPS webhook/callback routes and WSS media-streaming routes.
4. **Auth must be enabled in deployed environments**
   - `Security__RequireAuth=true` is the deployment target posture for API routes and SignalR once the web-to-API auth flow is chosen.
5. **Least privilege**
   - Disable ACR admin user.
   - Grant only required data-plane roles to ACA/App Service identities.
6. **Transcript privacy**
   - Avoid logging raw transcripts, phone numbers, or translated content into App Insights unless redacted and retention-bounded.

## Resource inventory for quota checks

| Resource / asset | Qty | Proposed shape | Region / scope | Quota / validation focus |
| --- | --- | --- | --- | --- |
| Resource group | 1 | `rg-callcentertranscribe-swc-mx01` | `swedencentral` | Subscription-level deployment access |
| Azure Container Registry | 1 | Basic | `swedencentral` | Registry quota, image pulls, RBAC |
| ACA managed environment | 1 | Consumption profile is sufficient to start | `swedencentral` | Environment availability |
| ACA API app | 1 | 1 replica warm during demo | `swedencentral` | CPU/memory fit, ingress, WebSockets |
| App Service Plan | 1 | Small paid Linux SKU | `swedencentral` | Instance quota, TLS/health-check support |
| Web App | 1 | Single app | `swedencentral` | App settings, identity, outbound access |
| Azure AI Speech | 1 | Standard | `swedencentral` | Real-time transcription support |
| Azure AI Language | 1 | Standard | `swedencentral` | Sentiment API throughput only |
| Azure AI Foundry reasoning deployment | 1 | GPT-5.x regional standard | `swedencentral` | Exact model/version availability + TPM quota |
| Azure AI Foundry embeddings deployment | 1 | Minimal embedding model | `swedencentral` | Regional availability + TPM quota |
| Key Vault | 1 | Standard | `swedencentral` | RBAC, secret/cert count |
| Log Analytics workspace | 1 | Pay-as-you-go | `swedencentral` | Ingestion/retention cost |
| Application Insights | 1 | Workspace-based | `swedencentral` | Sampling/retention |
| ACS resource | 1 | Voice/Call Automation capable | Geography-based, same RG | Geography fit, calling eligibility |
| ACS phone number | 1 | Inbound PSTN demo number | Service asset | Country availability, billing eligibility |
| Event Grid subscription | 1 | ACS `IncomingCall` webhook | Global/system-topic semantics | Handshake/retry behavior |
| Azure AI Translator | 1 | Text Translation | Europe processing geography | Accept EU processing or block deployment |

## Bottom line

- If the requirement means **“put every regional ARM resource in Sweden Central and keep exceptions explicit,”** this architecture is the minimal viable POC shape.
- If the requirement means **“all data must be processed only in Sweden Central,”** the current accepted scope is blocked by **ACS** and **Translator**, and deployment should stop until the requirement or scope is changed.

- **Source:** `.squad/decisions/inbox/athrun-dashboard-redesign-review.md`

# Review: Agent-Assist Dashboard Visual Redesign

**Date:** 2026-06-08T09:48:02.673-04:00
**Reviewer:** Athrun (Lead / Architect)
**Subject:** Lunamaria's visual/layout redesign — `Index.cshtml` + `site.css`
**Verdict:** ⛔ REQUEST CHANGES

---

## Summary

The redesign is well-crafted: the dark live-call header, speaker-turn accents, design-token system, and responsive layout are all solid work. One blocking accessibility violation prevents approval. Everything else passes.

---

## BLOCKING ISSUE — WCAG AA Contrast Failure

**File:** `src/CallCenterTranscription.Web/wwwroot/css/site.css`

**What:** `--cc-text-muted: #94a3b8` is used in multiple rules that render text on white and near-white card backgrounds. Measured contrast ratios:

| Background | Ratio | Required | Result |
|---|---|---|---|
| `--cc-surface` (`#ffffff`) | 2.56:1 | 4.5:1 | ✗ FAIL |
| `--cc-surface-2` (`#f6f8fc`) | 2.41:1 | 4.5:1 | ✗ FAIL |
| `--cc-agent-bg` (`#f0fdf4`) | 2.45:1 | 4.5:1 | ✗ FAIL |
| `--cc-cust-bg` (`#eff6ff`) | 2.36:1 | 4.5:1 | ✗ FAIL |

None of the affected elements qualify as large text (all ≤ 0.85rem, most 0.7–0.78rem).

**Affected CSS rules:**
- `.transcript-speaker-block p` — speaker role label (e.g. "Customer") on transcript card bg
- `.transcript-topline time` — timestamp on transcript card bg
- `.sentiment-score-label` — "Score" label on white card
- `.sentiment-meter-caption` — "0 = negative • 100 = positive" on white card
- `.translation-label` — "Translation (English)" on `#f0f7ff` tile
- `.panel-kicker` — feed mode badge text on `--cc-surface-2`
- `.sentiment-details dt` — "Tone", "Trend", "Updated" on tile surface
- `.mission-control-summary span` — summary labels on tile surface

**Why:** WCAG AA (4.5:1) is non-negotiable per project accessibility standard. Supplementary/metadata text is not exempt.

**Note:** `--cc-text-muted` via the `--cc-hdr-muted` rendered color on the dark navy header *does* pass (4.63–5.92:1 across the gradient range) — that usage is correct and must not be changed.

**Prescribed fix (minimal):**
Replace `color: var(--cc-text-muted)` with `color: var(--cc-text-secondary)` (`#475569`, 7.58:1 on white) in all of the light-surface rules listed above. The dark-header rules (`console-eyebrow`, `console-call-meta dt`, `console-status`) stay as-is — they use a different rendered color on the gradient and pass.

Alternatively, add a second token (e.g. `--cc-text-subtle: #6b7280`, 4.83:1) for light-surface secondary labels, preserving semantic intent. Either approach is acceptable.

**Assigned to: Meyrin** (per gate rule — the original author Lunamaria may not self-revise)

---

## PASSING CRITERIA

### 1. Correctness — JS selectors ✓
All selectors/IDs/data-attributes in `site.js` verified present in `Index.cshtml`:

| Selector | Present |
|---|---|
| `[data-console-refresh-root='true']` | ✓ line 12 |
| `[data-console-refresh-region]` | ✓ lines 20, 65, 235 |
| `[data-console-nav-view='true']` | ✓ lines 16, 230 |
| `[data-console-nav-toggle='true']` | ✓ lines 29, 242 |
| `[data-translation-toggle='true']` | ✓ line 118 |
| `[data-transcript-scroll='true']` | ✓ line 77 |
| `.mission-control-scroller` | ✓ line 283 |
| `.translation-panel` | ✓ line 147 |
| `#representative-view` | ✓ line 16 |
| `#mission-control-view` | ✓ line 230 |
| `h2[tabindex='-1']` (focus mgmt) | ✓ lines 24, 239 |

`data-speaker-role` is CSS-only; JS does not reference it. No breakage.

Razor syntax is clean; `dotnet build` reported 0 errors / 0 warnings.

### 2. Content preserved ✓
All panels intact: live-call header, transcript feed, sentiment panel, mission control view. No copy, mock data, or features removed or altered.

### 3. Accessibility — partial ✓ / ✗ (blocked above)
- Transcript: `role="log"` + `aria-live="polite"` + `aria-relevant="additions text"` ✓
- Connection status: `role="status"` + `aria-live="polite"` ✓
- Status/sentiment/churn NOT conveyed by color alone — all have text state labels, DL details, score numbers alongside color ✓
- Keyboard focus rings: `*:focus-visible` global override, plus explicit rings on scrollers, action links, nav buttons ✓
- `prefers-reduced-motion`: `*`, `*::before`, `*::after` all covered ✓
- Dark header text contrast: hdr-text `#e8f0fd` on gradient 10.01–14.08:1 ✓; muted rendered color on gradient 4.63–5.92:1 ✓
- Speaker heading colors: agent (`#065f46` on `#f0fdf4`) 7.34:1 ✓; customer (`#1d4ed8` on `#eff6ff`) 6.16:1 ✓
- **`--cc-text-muted` on light card backgrounds: 2.36–2.56:1 — FAILS AA ✗** (blocking, see above)

### 4. Security ✓
- No secrets in markup
- No new external CDN or third-party origins — Bootstrap and jQuery served from `~/lib/` (local libman)
- `_Layout.cshtml` unchanged

### 5. Quality ✓
- Design token system well-organized on `:root`
- Responsive breakpoints at 1200px / 992px / 768px; `100dvh` for mobile; `clamp()` fluid typography
- No new dependencies; system font stack is fast and correct
- Maintainable: clear section comments, semantic variable naming

---

## Nice-to-haves (non-blocking, post-fix)

- The `.panel-copy` descriptor text ("Diarization stays inline…") switches to `--cc-text-secondary` via the grouped rule which is correct, but visually it may feel heavier than intended once the muted bug is fixed. Consider a medium-weight token (e.g. `--cc-text-subtle: #6b7280`) as a distinct "secondary-light" tier if the design calls for visual hierarchy between labels and body copy.
- The 295px fixed side-column width may feel tight on 13" laptops at 100dvh. Worth a quick eyeball test during Phase 1 QA.

---

## Next action

Meyrin to fix the `--cc-text-muted` light-surface contrast failure in `site.css` and return for a re-gate by Athrun.

## RE-GATE: Dashboard Redesign — Accessibility Fix Verification
**Timestamp:** 2026-06-08T09:48:02.673-04:00
**Reviewer:** Athrun (Lead/Architect)
**Task:** Verify WCAG AA color contrast fix for light-card surfaces

### VERIFICATION RESULTS

✅ **Light-Surface Rules (CSS Color Contrast Fix):**
All 8 required light-card selectors now use `--cc-text-secondary` (#475569, ~7.58:1 contrast):
- `.transcript-speaker-block p` ✓
- `.transcript-topline time` ✓
- `.sentiment-score-label` (combined rule) ✓
- `.sentiment-meter-caption` ✓
- `.translation-label` ✓
- `.panel-kicker` ✓
- `.sentiment-details dt` ✓
- `.mission-control-summary span` ✓

✅ **Dark-Header Rules (Unchanged, Correct):**
All 3 dark-header selectors retain `--cc-hdr-muted` (appropriate for dark gradient):
- `.console-eyebrow` ✓
- `.console-call-meta dt` ✓
- `.console-status` ✓

✅ **No Unintended Changes:**
- Removed deprecated hardcoded colors (#5b6474, #475569, #64748b, etc.)
- All additions use the new design token system (--cc-text-primary, --cc-text-secondary, --cc-text-muted)
- No light-surface selector uses `--cc-text-muted` (#94a3b8) — the problematic token causing WCAG AA failure

### VERDICT: **APPROVE** ✓

The accessibility fix is complete and correct. All WCAG AA 4.5:1 contrast requirements are met for light-card surfaces. The color token swap maintains proper visual hierarchy while restoring compliance.

---

- **Source:** `.squad/decisions/inbox/athrun-frontend-deploy-oidc-least-privilege.md`

# 2026-06-07T06:38:03.974-04:00 — Frontend deploy uses OIDC with Web App-scoped RBAC

- **By:** Athrun / Coordinator
- **Decision:** GitHub Actions frontend deployment uses Azure workload identity federation for `sp-call-center` and avoids storing a client secret. The service principal is scoped to the frontend App Service with `Website Contributor` instead of resource-group-wide `Contributor`.
- **Why:** This satisfies the frontend-only deployment requirement while preserving least privilege and reducing credential leakage risk.
- **Source evidence:** `.github/workflows/deploy-frontend.yml`, GitHub repository secrets/variables, Azure federated credential `github-production-frontend`.

- **Source:** `.squad/decisions/inbox/bicep-specialist-azure-infra.md`

# Bicep Specialist Azure Infra Decision

- **When:** 2026-06-06T15:29:41.750-04:00
- **By:** Bicep Specialist
- **What:** Generated `infra/` Bicep for the approved Sweden Central resource group plan, but intentionally deferred Event Grid automation and Azure AI Foundry project/model deployment. The template provisions a regional Azure AI Services account now and outputs the manual follow-up steps.
- **Why:** The current app codebase does not yet expose a validated ACS incoming-call callback/WebSocket surface, so creating Event Grid resources now would be unsafe. Azure AI project/model deployment remains safer as a post-provision step than guessing unstable resource shapes or model contracts.
- **Impact:** Infrastructure is build-valid and ready for later `azure-validate` / deployment handoff, with explicit manual follow-up for ACS eventing, ACS data-plane RBAC, and AI model deployment.

- **Source:** `.squad/decisions/inbox/copilot-directive-2026-06-06T15-18-26-672-04-00.md`

### 2026-06-06T15:18:26.672-04:00: User directive
**By:** Jason (via Copilot)
**What:** Use a user-assigned managed identity for ACR pulls from Azure Container Apps; use the Container App system-assigned managed identity for runtime operations from ACA to other Azure services.
**Why:** User architecture guidance for correct Azure Container Apps identity separation.

- **Source:** `.squad/decisions/inbox/copilot-directive-2026-06-07T06-38-03-974-04-00.md`

### 2026-06-07T06:38:03.974-04:00: User directive
**By:** Jason (via Copilot)
**What:** Use GitHub Actions OIDC federation for Azure deployment authentication if possible, instead of storing a service principal client secret.
**Why:** User request — captured for team memory

- **Source:** `.squad/decisions/inbox/dyakka-acs-real-call-resource-floor.md`

# 2026-06-06T15:20:19.297-04:00 — ACS real-call resource floor and manual gates

- **By:** Dyakka
- **Decision proposal:** For the live ACS path, treat the deploy-now telephony set as:
  1. **Azure Communication Services** resource for telephony / Call Automation, with residency set via **`dataLocation` in Europe**. Do **not** represent this resource as Sweden-Central-hosted compute; ACS is a global/geography-scoped service.
  2. **Azure Container Apps environment + API app** in **`swedencentral`** with external HTTPS ingress for the Event Grid / Call Automation webhook endpoints and a real **WSS** endpoint for ACS media streaming.
  3. **Event Grid subscription** for `IncomingCall` routed to the API webhook. Because the ACS source is global, the corresponding **system topic is global**, not Sweden Central.
  4. **Managed identity** on the API host (system-assigned is fine; user-assigned only if the team wants a reusable principal) with direct RBAC on the ACS resource.
  5. **Sweden-Central observability** for the API path (for example, Log Analytics / Application Insights) so webhook failures, media-streaming startup, and Event Grid delivery issues are diagnosable.
- **Why this matters to team:** This keeps the squad's Option-A telephony topology intact while preventing a bad compliance story. The team can keep all region-bound compute and diagnostics in Sweden Central without overstating ACS/Event Grid location semantics.
- **Manual gate / prerequisite:** The demo depends on acquiring a **Swedish ACS phone number** (local or toll-free) on a **paid** Azure subscription whose **billing location** is one of Microsoft's eligible countries for Sweden numbers. Number search/purchase can be portal-driven or API-driven, but special-order / regulatory handling may still be manual.
- **Security / ops note:** The current API has no ACS webhook or media-streaming routes yet. When they are added, secure Event Grid delivery beyond `SubscriptionValidationEvent` (Microsoft Entra-protected webhook or shared secret), validate ACS mid-call JWTs, and keep the telephony API at **`minReplicas = 1`** during demo windows so a 30-second ringing call is not lost to cold start.
- **Source evidence:** `.squad/decisions.md`, `.azure/deployment-plan.md`, `src/CallCenterTranscription.Api/Program.cs`, `src/CallCenterTranscription.Telephony/IAudioSource.cs`, `https://learn.microsoft.com/en-us/azure/communication-services/concepts/privacy`, `https://learn.microsoft.com/en-us/azure/communication-services/concepts/authentication`, `https://learn.microsoft.com/en-us/azure/communication-services/how-tos/managed-identity`, `https://learn.microsoft.com/en-us/azure/communication-services/concepts/call-automation/incoming-call-notification`, `https://learn.microsoft.com/en-us/azure/communication-services/how-tos/call-automation/secure-webhook-endpoint`, `https://learn.microsoft.com/en-us/azure/communication-services/concepts/call-automation/audio-streaming-concept`, `https://learn.microsoft.com/en-us/azure/communication-services/concepts/numbers/phone-number-management-for-sweden`, `https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/telephony/get-phone-number`, `https://learn.microsoft.com/en-us/azure/event-grid/security-authentication`, `https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview`, `https://learn.microsoft.com/en-us/azure/container-apps/scale-app`, `https://learn.microsoft.com/en-us/azure/templates/microsoft.communication/communicationservices`.

- **Source:** `.squad/decisions/inbox/identity-split-fixer-aca-acr-uami.md`

# Decision: Split ACA identities (UAMI for ACR pull, system MI for runtime)

- **Date:** 2026-06-06T16:30:27.949-04:00
- **Requested by:** Jason
- **Scope:** Azure Container Apps identity design for deployment recovery

## Context
`azd provision --no-prompt` previously stalled with `ca-api-cctrans-kdarok` in `InProgress` and no ready revision. The API Container App was configured with system-assigned identity for both runtime operations and ACR pulls, while also provisioning with a public placeholder image.

## Decision
1. Introduce a **user-assigned managed identity (UAMI)** in Sweden Central dedicated to ACR pulls.
2. Configure API Container App identity as **`SystemAssigned,UserAssigned`**.
3. Keep **runtime service-to-service RBAC** (Key Vault, Cognitive Services, ACS-later) on the API Container App **system-assigned** principal.
4. Grant **`AcrPull`** at ACR scope to the **UAMI principal** only.
5. Make ACA `registries` auth **conditional**:
   - If `apiContainerImage` uses deployment ACR login server, bind `registries.identity` to UAMI resource ID.
   - Otherwise (placeholder/public image), keep `registries` empty to avoid unnecessary auth wiring during bootstrap.

## Why
- Enforces least-privilege identity boundaries by separating runtime Azure access from container image pull auth.
- Aligns with user directive and reduces risk of ACA revision readiness issues during placeholder-image provisioning.
- Preserves safe bootstrap defaults (`apiContainerImage` placeholder and `enableApiHealthProbes=false`).

## Implementation Notes
- `infra/main.bicep` updated with:
  - New `Microsoft.ManagedIdentity/userAssignedIdentities` resource for ACR pull.
  - API Container App dual identity attachment.
  - Conditional `registries` block using UAMI only for ACR-hosted images.
  - AcrPull role assignment module invocation switched to UAMI principal.
  - Outputs/manual post-provision guidance updated for identity split.
- `infra/modules/acr-pull-role-assignment.bicep` retained as-is (already generic).

## Recovery Guidance
- Do **not** run `azd provision` until validation is rerun and deployment window is approved.
- On next provision/deploy cycle, ensure API image reference is either:
  - placeholder public image (no ACR registries binding), or
  - `${ACR_LOGIN_SERVER}/api:<tag>` (registries uses UAMI for pull).

## Validation Requirements
- Required local checks after this change:
  - `az bicep build --file infra/main.bicep --stdout`
  - `dotnet build CallCenterTranscription.sln --nologo`
  - `dotnet test CallCenterTranscription.sln --no-build --nologo`
- `azure-validate` must be rerun before any `azure-deploy`.

- **Source:** `.squad/decisions/inbox/lacus-swedencentral-ai-resource-floor.md`

# 2026-06-06T15:20:19.363-04:00 — Sweden Central AI resource floor for the POC

- **By:** Lacus
- **Decision proposal:** For the current POC, keep the Azure AI deploy-now set to:
  1. **Azure AI Speech** single-service resource in `swedencentral` for real-time STT, diarization, and language detection.
  2. **Microsoft Foundry** resource in `swedencentral`, with one project and one **regional** mid-tier reasoning deployment behind `IReasoningClient` (default to a quota-approved `gpt-5-mini`; keep MAI swap-ready later). Fall back to **DataZone EU** only if Sweden Central regional quota blocks the POC.
  3. **Azure Translator** single-service resource on the **Global** endpoint for non-English text normalization, because Translator regional endpoints do not support Microsoft Entra authentication.
- **Why this matters to team:** This preserves the accepted Speech + Translator + Foundry direction, keeps the mocked-RAG POC lean, and avoids paying for AI Search / extra model deployments before grounded churn and next-best-action flows are validated end to end.
- **Auth / ops note:** API and Web should use system-assigned managed identities. Grant the API managed identity **Cognitive Services User** on Speech, Foundry, and Translator. Speech keyless auth also requires a custom domain. Only feed **final/coalesced** transcript turns into `IReasoningClient`; sending interim hypotheses will inflate Foundry quota usage and trace volume.
- **Deferred for now:** Azure AI Search, separate embedding deployment, Azure AI Language sentiment resource, Custom Speech, Content Safety, and Document Translation.
- **Caveat:** The clean keyless Translator path uses the **Global** endpoint, so it is not Sweden-Central-scoped processing. If strict in-region translation becomes mandatory, the team must explicitly choose between a regional Translator + Key Vault-backed key exception or deferring translation until the platform supports a regional keyless path.
- **Source evidence:** `.squad/decisions.md`, `.azure/deployment-plan.md`, `src/CallCenterTranscription.Ai/IReasoningClient.cs`, `src/CallCenterTranscription.Ai/MockReasoningClient.cs`, `src/CallCenterTranscription.Shared/Events/TranscriptEvent.cs`, `https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions`, `https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-configure-azure-ad-auth`, `https://learn.microsoft.com/en-us/azure/ai-services/multi-service-resource`, `https://learn.microsoft.com/en-us/azure/foundry/how-to/create-projects`, `https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability`, `https://learn.microsoft.com/en-us/azure/ai-services/translator/how-to/create-translator-resource`, `https://learn.microsoft.com/en-us/azure/ai-services/translator/how-to/microsoft-entra-id-auth`.

- **Source:** `.squad/decisions/inbox/lunamaria-dashboard-redesign.md`

# Decision: Agent-Assist Dashboard Visual Redesign

**Date:** 2026-06-08T09:48:02.673-04:00  
**Author:** Lunamaria (Frontend Dev)  
**Status:** Implemented

---

## Context

The initial rep-console (`Index.cshtml`) shipped with solid semantics and a functional two-column layout, but the visual language was plain — uniform white cards, no speaker differentiation in the transcript, no live-call signal in the header, and a sentiment meter that didn't command immediate attention. The task was a visual redesign with no content changes.

---

## Research: Patterns from Real Agent-Desktop Products

Reference products surveyed: Salesforce Service Cloud / Einstein, Zendesk Agent Workspace, Genesys Cloud, Five9, Cresta, Observe.AI, Talkdesk, Gladly, Intercom Fin.

### 4 patterns adopted and why:

**1. Dark "live call" header bar (Genesys Cloud, Salesforce Service Console)**  
Genesys and Salesforce both use a visually distinct header zone to signal "you are actively on a call." A dark-navy gradient (`#0c1e4a → #1a3380`) on the call-context card achieves this — the agent can't mistake the current call state. The call meta tiles (Call ID / Customer / Connected) sit inside frosted-glass-style tiles on the dark background, reading as data-dense but calm.

**2. Speaker-turn visual differentiation (Cresta, Observe.AI, Talkdesk)**  
Every serious transcript UI distinguishes customer vs. agent turns by more than a name badge. Cresta uses color-coded left border accents. I adopted:  
- Customer turns: blue left border (`#2563eb`) + light blue bg (`#eff6ff`)  
- Agent turns: green left border (`#059669`) + light green bg (`#f0fdf4`)  
This lets a rep scan "who said what" in one glance without reading names.

**3. Calm status-by-color-AND-icon-AND-text system (Intercom Fin, Gladly)**  
Intercom Fin uses a minimal color palette with near-no saturation on non-status content. Status always gets color + icon/shape + text — never color alone. My token system codifies this: `--cc-ok/warn/danger` plus semantic `-light`/`-text` variants, applied to meter bars, alert states, and speaker accents. The sentiment meter uses these same tokens so there's no invented palette.

**4. Live pulse on active state (Observe.AI, Cresta)**  
A small animated dot (`.console-status::before`) pulses green while the call is connected — the agent gets a constant peripheral confirmation the stream is live. Animation respects `prefers-reduced-motion`.

---

## Decisions Made

| Decision | Rationale |
|---|---|
| Dark navy header, light card body | Clear "live call" signal without theming the whole page |
| CSS custom properties design-token set on `:root` | Single source of truth; easy to update brand later |
| Speaker-role via `data-speaker-role` HTML attribute | Structural change only — no content change, no JS impact |
| Removed `border-left`/`padding-left` from `.console-side-column` | The sentiment `.card-shell` provides its own chrome; the separator was visual noise |
| `panel-header` gets `border-bottom` separator | Separates panel heading from content without adding a new wrapper element |
| Kept Bootstrap loaded | Used only for `.visually-hidden`, `.btn`, `.mb-0` — Bootstrap handles its own concerns; our token system handles the console look |
| No external fonts or CDNs added | System font stack (`-apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui`) is fast, zero dependency, and matches native OS rendering |

---

## Files Changed

- `src/CallCenterTranscription.Web/wwwroot/css/site.css` — complete token-based rewrite
- `src/CallCenterTranscription.Web/Pages/Index.cshtml` — added `data-speaker-role` attribute to transcript `<li>` items

## Files Verified Unchanged (JS hooks intact)

All `site.js` data-attribute selectors confirmed present in post-edit Index.cshtml:
`data-console-refresh-root`, `data-console-refresh-region` (header/columns/mission), `data-console-nav-view`, `data-console-nav-toggle`, `data-translation-toggle`, `data-transcript-scroll`, `.mission-control-scroller`, `.translation-panel`, `#representative-view`, `#mission-control-view`.

## Build Result

`dotnet build CallCenterTranscription.sln` → **Build succeeded. 0 Warning(s). 0 Error(s).** (12.4s)

- **Source:** `.squad/decisions/inbox/lunamaria-frontend-deploy-workflow.md`

# 2026-06-07T06:29:29.980-04:00 — Frontend-only App Service deploy workflow

- **By:** Lunamaria
- **Decision proposal:** Standardize frontend-only deployment on `.github/workflows/deploy-frontend.yml` with push-to-`main` path filters and manual dispatch. Build/publish only `src/CallCenterTranscription.Web/CallCenterTranscription.Web.csproj`, then deploy the artifact to the existing App Service using GitHub OIDC federation and repo-scoped Azure identifiers instead of hardcoded values.
- **Why this matters to team:** It lets UI-only changes ship without touching ACA/API resources, keeps the frontend pipeline small and fast, and reduces accidental infrastructure churn while backend deployment remains separate.
- **Operational note:** The workflow verifies the target App Service exists in the configured resource group before deployment. It expects `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` repository secrets, plus `AZURE_WEBAPP_NAME` and `AZURE_RESOURCE_GROUP` repository variables.
- **Source evidence:** `azure.yaml`, `src/CallCenterTranscription.Web/CallCenterTranscription.Web.csproj`, `infra/main.bicep`, `README.md`, `.github/workflows/deploy-frontend.yml`

- **Source:** `.squad/decisions/inbox/meyrin-azd-api-docker-context.md`

# 2026-06-06T15:29:41.673-04:00 — AZD API Docker context set to repo root

- **By:** Meyrin
- **Decision proposal:** Keep `azure.yaml` API service as `project: src/CallCenterTranscription.Api` with `docker.path: ./Dockerfile`, and set `docker.context: ../..` so Docker builds from repository root.
- **Why this matters to team:** `CallCenterTranscription.Api` depends on sibling projects (`Shared`, `Ai`, `Telephony`), so project-local Docker contexts cannot copy all build inputs. Root context keeps restore/publish deterministic for local and CI `azd` workflows.
- **Operational note:** API Dockerfile publishes .NET 9 app on port `8080` and runs as non-root runtime user (`USER $APP_UID`).
- **Source evidence:** `azure.yaml`, `src/CallCenterTranscription.Api/Dockerfile`, `src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj`.

- **Source:** `.squad/decisions/inbox/meyrin-contrast-fix.md`

# 2026-06-08T09:48:02.673-04:00 — Meyrin contrast fix

Swapped `color: var(--cc-text-muted)` (#94a3b8, fails WCAG AA 4.5:1) → `color: var(--cc-text-secondary)` (#475569, 7.58:1) on light-card rules `.panel-kicker`, `.translation-label`, `.sentiment-score-label`, `.sentiment-meter-caption`, `.sentiment-details dt`, `.mission-control-summary span`, `.transcript-speaker-block p`, and `.transcript-topline time`; dark-header rules (`.console-eyebrow`, `.console-call-meta dt`, `.console-status`) left untouched on `--cc-hdr-muted`; resolves Athrun's a11y gate.

- **Source:** `.squad/decisions/inbox/meyrin-deployment-artifacts-aca-appservice.md`

# 2026-06-06T15:20:19.390-04:00 — Deployment artifact split (ACA API + App Service Web)

- **By:** Meyrin
- **Decision proposal:** For the current POC hosting direction, standardize deployment packaging as:
  1. **API (`CallCenterTranscription.Api`)** on **Azure Container Apps** via container image (Dockerfile required).
  2. **Web (`CallCenterTranscription.Web`)** on **Azure App Service** via source/package deploy (no Web Dockerfile required for now).
- **Why this matters to team:** This locks CI/CD and IaC shape for Lunamaria/Lacus integration points and avoids running two container supply chains when only API needs ACA.
- **Operational note:** API already has `/healthz`; Web currently has no health endpoint, so add one before enabling App Service Health Check.
- **Security follow-up (required):** `Security__RequireAuth=true` is not deployment-ready until the team chooses and implements a concrete auth model (for example, Entra-authenticated Web and JWT bearer validation on API/SignalR) plus corresponding app settings.
- **Source evidence:** `src/CallCenterTranscription.Api/Program.cs`, `src/CallCenterTranscription.Web/Program.cs`, `src/CallCenterTranscription.Web/Services/BackendApiOptions.cs`, `.squad/decisions.md`.

- **Source:** `.squad/decisions/inbox/meyrin-healthcheck-forwarded-headers.md`

# 2026-06-06T15:29:41.673-04:00 — Health checks must bypass HTTPS redirect behind Azure proxies

- **By:** Meyrin
- **Decision proposal:** For API and Web hosted behind Azure reverse proxies (Container Apps/App Service), apply forwarded headers middleware before HTTPS redirection and exempt `/healthz` from redirect.
- **Why this matters to team:** This keeps platform health probes deterministic (direct 200 on `/healthz`) while preserving HTTPS redirect behavior for user-facing routes.
- **Operational note:** Both services now configure `X-Forwarded-For` and `X-Forwarded-Proto` handling in startup and keep `/healthz` mapped as an anonymous minimal endpoint.
- **Source evidence:** `src/CallCenterTranscription.Api/Program.cs`, `src/CallCenterTranscription.Web/Program.cs`.

- **Source:** `.squad/decisions/inbox/rbac-idempotency-fixer-role-assignment-guid-seeds.md`

# Decision: RBAC Role Assignment GUID Seeds Must Include Principal ID

- **Decision ID:** rbac-idempotency-fixer-role-assignment-guid-seeds
- **Date:** 2026-06-06T16:36:28.287-04:00
- **Requester:** Jason
- **Revision Author:** rbac-idempotency-fixer

## Context
Security/deployment review rejected the prior revision because role assignment names were seeded with principal/resource names instead of principal IDs. This is not recovery-safe when managed identities are recreated and receive new principal IDs.

## Decision
1. Runtime role assignments in `infra/main.bicep` now use:
   - `guid(scope.id, apiContainerApp.identity.principalId, roleDefinitionId)`
2. `infra/modules/acr-pull-role-assignment.bicep` now seeds role assignment name with:
   - `guid(registry.id, principalId, acrPullRoleDefinitionId)`
3. The module parameter `principalName` is removed; callers pass only `principalId`.

## Constraints Preserved
- ACA **user-assigned** identity remains dedicated to ACR pulls.
- ACA **system-assigned** identity remains dedicated to runtime Key Vault/Cognitive/ACS RBAC.
- No secrets added.
- No resource deletion.

## Expected Outcome
Role assignment resources become idempotent and recovery-safe across identity recreation events because GUID seeds now track the effective principal object ID.

## Validation State
- Bicep compile/build/test commands rerun in this revision.
- Full `azure-validate` remains pending; deployment plan status is set to **Ready for Re-Validation**.

- **Source:** `.squad/decisions/inbox/revision-engineer-deployment-readiness-fixes.md`

# Revision Engineer — Deployment Readiness Fixes

- **When:** 2026-06-06T15:55:10-0400
- **By:** Revision Engineer
- **What:** Updated Azure deployment artifacts to make ACA bootstrap safe with `enableApiHealthProbes=false` by default, tightened Key Vault firewall posture to `defaultAction=Deny` (`bypass=AzureServices`), and removed ACS live readiness claims by deferring Event Grid/callback/media automation until API routes are implemented.
- **Why:** Security review rejected prior artifacts for unsafe placeholder-health coupling, implied ACS live readiness without routes, and permissive Key Vault firewall defaults.
- **Impact:** Infrastructure remains provisionable for POC resource floor while avoiding false ACS-live readiness claims; post-provision gate now explicitly requires real API image deployment and `/healthz` verification before enabling ACA probes.

- **Source:** `.squad/decisions/inbox/yzak-azure-deployment-readiness.md`

# Yzak Review — Azure Deployment Readiness

**Date:** 2026-06-06T15:20:19.350-04:00  
**Reviewer:** Yzak  
**Verdict:** REJECT

## What I reviewed

- Established Squad decisions in `.squad/decisions.md`
- Current planning artifact in `.azure/deployment-plan.md`
- Current app/runtime seams in:
  - `src/CallCenterTranscription.Api/Program.cs`
  - `src/CallCenterTranscription.Api/appsettings.json`
  - `src/CallCenterTranscription.Web/Program.cs`
  - `tests/CallCenterTranscription.Tests/ApiWiringSmokeTests.cs`

## Bottom line

The Azure direction itself is still fine: backend on Azure Container Apps, frontend on App Service, real ACS in the final demo, and mock audio as the fallback.  
What is **not** fine is pretending the deployment plan is ready. Right now it is just a placeholder checklist plus RG/region. That is nowhere near enough for a live-demo gate.

## Evidence behind the rejection

1. **The deployment plan is skeletal, not a deployment direction you can trust.**
   - `.azure/deployment-plan.md` only records planning status, resource group, location, and an unchecked checklist.
   - It does **not** define service topology details, auth choices, secrets posture, health strategy, validation gates, rollback criteria, or demo runbook checkpoints.

2. **Security posture is not defined where it matters.**
   - Squad decisions and agent history consistently require managed identity and no secrets in code.
   - The plan does not say which identities exist, which Azure roles they need, which services use direct Entra auth, or when Key Vault is required for unavoidable secrets.
   - `src/CallCenterTranscription.Api/appsettings.json` still defaults `Security:RequireAuth` to `false`, and the plan does not define the production override or front-end/back-end auth approach.

3. **Health coverage is too thin for a live demo.**
   - `src/CallCenterTranscription.Api/Program.cs` has a basic `/healthz` endpoint.
   - `src/CallCenterTranscription.Web/Program.cs` exposes no explicit health endpoint at all.
   - The plan does not define readiness/liveness expectations for ACA, warmup/health behavior for App Service, or dependency-aware checks for ACS callback/media flow.

4. **ACS callback/WebSocket reliability is not gated.**
   - Squad decisions require public callback/WebSocket endpoints on ACA for the real ACS path.
   - The plan does not define validation for inbound callback reachability, Event Grid handshake, media WebSocket behavior, reconnect handling, or the dress-rehearsal sequence for the rep/customer call flow.

5. **Validation gates are incomplete.**
   - Current baseline is good: `dotnet build CallCenterTranscription.sln` passed and `dotnet test CallCenterTranscription.sln --no-build` passed (4/4).
   - But those only prove scaffold health. They do not prove Azure runtime readiness, managed identity access, public ingress behavior, or demo survivability.

## Required changes before this gets out of QA jail

1. **Document the production auth + secret model**
   - State that ACA and App Service use managed identity by default.
   - List required RBAC per service (ACS, Azure AI Speech, Translator, AI Foundry, Key Vault if used).
   - State explicitly that connection strings/API keys are forbidden in code and appsettings; if any secret is unavoidable, it must come from Key Vault.
   - Define the production setting that enables auth for API routes/hub and how the web app authenticates to the API.

2. **Add a real health strategy**
   - Define API liveness/readiness beyond a static `200 OK`.
   - Define an explicit frontend health endpoint or warmup probe strategy for App Service.
   - Define what “ready” means for the live demo path: API up, SignalR negotiate works, ACS callback endpoint reachable, media WebSocket reachable.

3. **Add live-demo reliability gates**
   - Require one smoke path with `MockAudioSource` so the demo still runs if ACS flakes.
   - Require one real-ACS dress rehearsal covering inbound answer, media streaming start, rep add, and visible transcript updates.
   - Define clear go/no-go criteria and fallback steps.

4. **Add post-deploy validation gates**
   - Web health check
   - API health check
   - SignalR negotiate smoke test
   - ACS callback validation
   - Media WebSocket validation
   - Mock-audio end-to-end transcript smoke test

5. **Add operational guardrails**
   - Minimum warm instances / anti-cold-start posture for the demo window
   - Logging/telemetry checkpoints needed to debug a failed rehearsal fast
   - Cost/budget note for ACS + AI service usage so the POC does not surprise-bill itself during repeated rehearsals

## Reviewer note

If this were only an architecture-direction checkpoint, I could live with **APPROVE-WITH-CHANGES**.  
As a **deployment readiness** checkpoint, this stays **REJECT** until the missing security, health, and validation gates are written down.

Second-pass devil's-advocate review agreed with that call: the missing items are core acceptance criteria, not cleanup.



# 2026-06-08T10:57:44.227-04:00 — Agent Console 80/20 Column Layout + Mission Control Link

- **By:** Lunamaria
- **Status:** APPROVED by Athrun
- **What:** Reworked the Agent Console from equal-width columns (pill-nav) to an 80/20 split: transcript occupies 4fr (80%), metadata (sentiment, future panels) occupies 1fr (20%). Converted "Mission Control" from a competing pill button to a dimmed text link in the top nav bar with subtle active/hover styling. Moved sentiment display into the narrower right column. Ensured full remaining height (100dvh chain) with page `overflow: hidden` and internal scroll only on `.transcript-scroller`.
- **Why:** Jason's feedback emphasized transcript focus (80% visual real estate) and reduced cognitive load (Mission Control as link, not tab). The narrower metadata column enables sentiment at a glance while keeping scrollable space for future panels (knowledge cards, churn meter, next-step guidance).
- **Files:** `src/CallCenterTranscription.Web/wwwroot/css/site.css`, `src/CallCenterTranscription.Web/Pages/Index.cshtml`
- **Validation:** `dotnet build ... -c Release --nologo` → 0 errors. All site.js selectors verified. WCAG AA contrast preserved (~5.88:1).
- **Non-blocking notes:** Unicode arrow on "Mission Control →" could wrap in `<span aria-hidden>` for screen readers; mobile at `<768px` (3fr 1fr = 75%/25%) may squeeze sentiment text but not a blocker.
- **Source:** `.squad/decisions/inbox/lunamaria-console-80-20-columns.md`

# 2026-06-08T10:57:44.227-04:00 — Review: Agent Console 80/20 Column Layout + Mission Control Link

- **By:** Athrun
- **Verdict:** ✅ APPROVE
- **Criteria:** All 7 pass: 80/20 columns (4fr 1fr), full height + internal scroll (100dvh → flex:1 views → transcript-scroller flex:1 overflow-y:auto, body overflow:hidden), Mission Control link (styled as dimmed text, underline on hover, border-bottom on active, still a button with aria-controls, :focus-visible intact), JS hook integrity (site.js untouched, all data-* verified), content preserved (sentiment/Mission Control/transcript), colors/AA (no tokens changed, ~5.88:1 contrast), security (no secrets/external assets).
- **Why:** The change cleanly delivers Jason's requirements without regressions. Build passed, visual hierarchy is now transcript-dominant, and the console remains keyboard-accessible and screen-reader compatible.
- **Source:** `.squad/decisions/inbox/athrun-console-80-20-review.md`

# 2026-06-08T12:05:43.410-04:00 — GitHub Actions Node20 → Node24 bump

- **By:** Meyrin
- **Type:** CI/CD maintenance
- **Decision:** Bump all five flagged actions to their current latest major that declares `using: 'node24'`, and resolve each new major tag to its exact 40-char upstream commit SHA (annotated tags dereferenced to the underlying commit). Apply the same SHA-pinning to the floating-tag checkout references in the squad workflows.
- **Rationale:** GitHub announced Node.js 24 enforcement on 2026-06-16 and removal of Node.js 20 from runners on 2026-09-16. Five actions in `.github/workflows/deploy-frontend.yml` were pinned to major versions declaring `using: 'node20'`. Four squad automation workflows carried floating-tag `actions/checkout@v4` references (Node20 era, no SHA pin). Bumping to Node24-compatible majors and SHA-pinning improves supply-chain posture ahead of the deadline.
- **Actions bumped (all SHA-pinned):**
  - `actions/checkout` v4 → v5 (SHA: `93cb6efe18208431cddfb8368fd83d5badbf9bfd`)
  - `actions/setup-dotnet` v4 → v5 (SHA: `9a946fdbd5fb07b82b2f5a4466058b876ab72bb2`)
  - `actions/upload-artifact` v4 → v7 (SHA: `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a`)
  - `actions/download-artifact` v4 → v8 (SHA: `3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c`)
  - `azure/login` v2 → v3 (SHA: `532459ea530d8321f2fb9bb10d1e0bcf23869a43`)
- **Workflows modified:** `deploy-frontend.yml`, `squad-heartbeat.yml`, `squad-issue-assign.yml`, `squad-triage.yml`, `sync-squad-labels.yml`
- **Residual risk (non-blocking):** `actions/github-script@v7` remains floating in squad workflows (pre-existing, not in deprecation scope); `azure/webapps-deploy@v3` still Node20 era (monitor for future Node24 release).
- **Status:** Committed as 9c6c32c (8 days ahead of enforcement deadline).
- **Source:** `.squad/decisions/inbox/meyrin-actions-node24-bump.md`

# 2026-06-08T12:05:43.410-04:00 — Review: GitHub Actions Node20 → Node24 bump

- **By:** Athrun (Reviewer gate)
- **Verdict:** ✅ APPROVE
- **Criteria met:**
  1. **NODE24 COVERAGE** ✅ — All 5 flagged actions bumped; version choices correct (upload-artifact v7, download-artifact v8, azure/login v3 are first Node24 releases).
  2. **SHA INTEGRITY** ✅ — All 5 SHAs independently verified against upstream (including dereferenced annotated tag for azure/login).
  3. **NO FLOATING TAGS (bumped only)** ✅ — All bumped actions are 40-char SHA-pinned with trailing version comments.
  4. **NO LOGIC DRIFT** ✅ — Only `uses:` lines changed; no trigger, permissions, env, step, or with-arg modifications.
- **Non-blocking follow-ups:** `actions/github-script@v7` floating in 6 squad workflow locations (pre-existing, supply-chain hygiene follow-up); `azure/webapps-deploy@v3` monitor for Node24 release.
- **Source:** `.squad/decisions/inbox/athrun-actions-node24-review.md`

# 2026-06-08T12:58:45.624-04:00 — Decision: Mission Control Promoted to Separate Razor Page

**Author:** Lunamaria (Frontend Dev)  
**Requested by:** Jason  
**Status:** Implemented

---

## Context

Mission Control was previously an in-page hidden `<section>` inside `Index.cshtml`, toggled visible by a JavaScript `setActiveView` function when the user clicked a "Mission Control →" nav button. Jason confirmed the layout looked good but requested that Mission Control become a proper separate page rather than an in-page toggle.

---

## Decision

Promote Mission Control from a JS-toggled hidden section to a real Razor Page at `/MissionControl`.

---

## Rationale

- **Real navigation > in-page toggle:** Browser back/forward work naturally. The URL is bookmarkable. Supervisor staff can link directly to Mission Control.
- **Cleaner code separation:** Each page owns its own data fetching (Index fetches transcript/sentiment/session; MissionControl fetches only `GetMissionControlHealthAsync`). No single page model carries both concerns.
- **Simpler JS:** The entire `setActiveView` / `getConsoleViews` / nav-toggle click-handler block is dead weight once navigation is real routes. Removing it reduces JS surface area and eliminates a class of potential bugs.
- **No added complexity:** Razor Pages tag helper `asp-page` does all routing. Zero new middleware, zero new JS.

---

## Implementation

### Files created
- `src/CallCenterTranscription.Web/Pages/MissionControl.cshtml` — view with same full-viewport shell (`console-page-shell` / `console-body` / `console-main` / `rep-console`), the Mission Control content verbatim, and a `← Agent Console` back-link.
- `src/CallCenterTranscription.Web/Pages/MissionControl.cshtml.cs` — `MissionControlModel` PageModel; calls only `GetMissionControlHealthAsync`; includes own `ToDisplayLabel` static to avoid cross-model view dependencies.

### Files modified
- **`Index.cshtml`:** Removed `<section id="mission-control-view">` and all its content. Replaced the two `<button data-console-nav-toggle>` pills with `<span aria-current="page">Agent Console</span>` + `<a asp-page="/MissionControl">Mission Control →</a>`.
- **`site.js`:** Removed `consoleViewSelector`, `consoleNavToggleSelector`, `getConsoleViews()`, `setActiveView()`, nav-toggle case in `getFocusRestoreKey`, `case "nav-toggle"` in `restoreFocus`, and the nav-toggle block in the global click handler. All transcript/sentiment/refresh/translation logic is intact and unchanged.
- **`site.css`:** Added `text-decoration: none` to `.screen-nav-btn` so `<a>` elements styled with that class don't show the default browser underline.

### Cross-link pattern
```html
<!-- Index.cshtml nav -->
<span class="screen-nav-btn" aria-current="page">Agent Console</span>
<a asp-page="/MissionControl" class="screen-nav-btn">Mission Control →</a>

<!-- MissionControl.cshtml nav -->
<a asp-page="/Index" class="screen-nav-btn">← Agent Console</a>
<span class="screen-nav-btn" aria-current="page">Mission Control</span>
```

### JS selectors preserved on Agent Console
| Selector | Purpose |
|---|---|
| `[data-console-refresh-root='true']` | Drives 4s DOM-swap refresh loop |
| `[data-console-refresh-region]` | `header` + `columns` regions swapped on refresh |
| `[data-transcript-scroll='true']` | Auto-scroll + state capture/restore |
| `[data-translation-toggle='true']` | Per-utterance translation reveal |
| `.translation-panel` | Expand/collapse target |
| `.mission-control-scroller` | Scroll-state capture (no-ops gracefully on Index) |

---

## Trade-offs / Rejected alternatives

- **Keep in-page toggle but use `<a>` with `event.preventDefault()`:** Rejected — adds JS complexity for zero benefit. Real routes are the right tool.
- **Add `data-console-refresh-root` to MissionControl page for auto-refresh:** Deferred — not requested. The page renders fresh data on each navigation. Can be added later as a simple attribute + the existing refresh loop handles it automatically.
- **Move `ToDisplayLabel` to a shared utility:** Reasonable refactor but out of scope for this task. Both IndexModel and MissionControlModel have their own copy; they're identical static functions. Can be extracted to a `DisplayHelpers` static class in a future cleanup pass.

---

## Build result

`dotnet build src/CallCenterTranscription.Web/CallCenterTranscription.Web.csproj -c Release --nologo` → **Build succeeded in 9.1s. 0 errors, 0 warnings.**

---

# 2026-06-08T12:58:45.624-04:00 — Review: Mission Control Separate Page

**Reviewer:** Athrun (Lead/Architect)  
**Author:** Lunamaria  
**Verdict:** REQUEST CHANGES  

---

## Critical Issue

**`site.js` line 275 — `translationButton` is undeclared (ReferenceError on every click).**

The nav-toggle removal accidentally also deleted the `const translationButton = target.closest(translationToggleSelector);` line. The surviving reference to `translationButton` in the click handler now throws `ReferenceError` on every click event on BOTH pages. This completely breaks translation toggles on the Agent Console and spams console errors on MissionControl.

## Minor Issue

**`site.js` line 101 — `case "transcript-scroller":` indentation is wrong** (shifted 8 spaces left vs. sibling cases after the nav-toggle case deletion). Cosmetic but should be fixed in the same pass.

## Passes

- ✓ MissionControl.cshtml is a real Razor Page (`@page`, `@model`, correct namespace)
- ✓ Content moved (not duplicated); Index is console-only
- ✓ Cross-links via `asp-page`; `_ViewImports` registers tag helpers → links resolve
- ✓ Build 0 errors, 0 warnings
- ✓ JS refresh loop early-exits cleanly on MissionControl (no `data-console-refresh-root`)
- ✓ No secrets, no new external assets
- ✓ `aria-live` on transcript preserved; no AA contrast regression
- ✓ PageModel namespace matches project convention

## Disposition

Assign fix to **Meyrin** (not Lunamaria — reviewer gate policy). Fix is surgical: restore the missing `const translationButton = target.closest(translationToggleSelector);` line and realign the switch case indentation.

**2026-06-08T12:58:45.624-04:00** — RE-GATE ✓ APPROVED: All fixes verified. Meyrin corrected both blocking issues. No new issues. `node --check` clean, build 0 errors.

---

# 2026-06-08T12:58:45.624-04:00 — Meyrin: site.js regression fix

**Status:** COMPLETED  
**What:** Restored the missing `const translationButton = target.closest(translationToggleSelector);` declaration before its `if (isHtmlElement(translationButton))` guard in the click handler (fixing the ReferenceError introduced when Lunamaria removed nav-toggle code), and re-aligned `case "transcript-scroller":` to match sibling case indentation in `restoreFocus`; `node --check` and `dotnet build` both pass clean.

---

# 2026-06-08T13:29:12.574-04:00 — Decision: /lib static-asset provisioning via libman + HTML no-cache middleware

**Author:** Meyrin (Backend Dev)  
**Status:** Implemented

## Context

Two production issues were identified on `web-cctrans-kdarok.azurewebsites.net`:

1. `/lib/bootstrap/…`, `/lib/jquery/…`, and `/lib/jquery-validation-unobtrusive/…` returned HTTP 404 because `wwwroot/lib/` contained only LICENSE files — no actual dist JS/CSS. No `libman.json` existed; the files were never restored before `dotnet publish`.
2. HTML document responses (Razor Pages) carried no `Cache-Control` header, causing Edge and other browsers to cache the HTML heuristically and show stale UI after deploys. Fingerprinted CSS/JS assets were fine; only the HTML doc went stale.

## Decision 1 — /lib asset provisioning via libman (not committed vendor blobs)

**Chosen approach:** libman restore at build time.

**Rationale:**
- Committing minified vendor blobs bloats git history, makes security audits harder, and creates friction when updating libraries. Rejected.
- Switching to a CDN link in `_Layout.cshtml` would change markup (out of scope) and introduce a hard dependency on CDN availability at runtime. Rejected.
- libman is the intended ASP.NET Core mechanism for client-side library management. Adding a `libman.json` and a workflow restore step keeps the build reproducible without committing vendor blobs and matches how the scaffold was designed to work.

**Files created:**
- `.config/dotnet-tools.json` — pins `microsoft.web.librarymanager.cli@3.0.71` as a dotnet local tool.
- `src/CallCenterTranscription.Web/libman.json` — declares four libraries via the `jsdelivr` provider (CDN-backed, no auth required):
  - `bootstrap@5.3.3` → `wwwroot/lib/bootstrap/` (CSS + bundle JS)
  - `jquery@3.7.1` → `wwwroot/lib/jquery/` (jquery.min.js)
  - `jquery-validation@1.21.0` → `wwwroot/lib/jquery-validation/` (jquery.validate.min.js)
  - `jquery-validation-unobtrusive@4.0.0` → `wwwroot/lib/jquery-validation-unobtrusive/` (jquery.validate.unobtrusive.min.js)

**Workflow change (`.github/workflows/deploy-frontend.yml`):**
A `run:` step — no new action, no new SHA pin needed — added between "Restore web project" and "Publish web project":

```yaml
- name: Restore client-side libraries (libman)
  working-directory: src/CallCenterTranscription.Web
  run: |
    dotnet tool restore --verbosity minimal
    dotnet tool run libman restore
```

`dotnet tool restore` finds `.config/dotnet-tools.json` by traversing up from `src/CallCenterTranscription.Web`. `libman restore` reads `libman.json` in the working directory and downloads from jsdelivr into `wwwroot/lib/`. These files are then included by `dotnet publish`.

**Security note:** jsdelivr is a well-established public CDN (jsDelivr). Libraries are version-pinned in `libman.json`. The libman CLI tool version is pinned in `dotnet-tools.json`. No executable scripts are pulled without version pinning. The `run:` step uses the project's own `dotnet tool` — no new SHA-pinned action needed.

**Verification:** Local run of `dotnet tool restore` + `libman restore` + `dotnet publish -c Release` confirmed all five files present in the publish output at real size:
- `wwwroot/lib/bootstrap/dist/css/bootstrap.min.css` — 227 KB
- `wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js` — 79 KB
- `wwwroot/lib/jquery/dist/jquery.min.js` — 85 KB
- `wwwroot/lib/jquery-validation/dist/jquery.validate.min.js` — 25 KB
- `wwwroot/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js` — 5.7 KB

## Decision 2 — HTML document Cache-Control: no-cache middleware

**Chosen approach:** Inline `app.Use` middleware with an `OnStarting` callback in `Program.cs`.

**Policy:** `Cache-Control: no-cache, no-store, must-revalidate` + `Pragma: no-cache` + `Expires: 0` on all `text/html` responses. `no-cache` allows the browser to store the response but requires revalidation (ETag round-trip) on every navigation — cheap and correct. `no-store` is added as belt-and-suspenders to prevent even intermediate caches from holding a stale HTML shell.

**Why not `no-cache` alone:** Both are fine; `no-store` is belt-and-suspenders for shared/enterprise proxies that may otherwise serve a stale HTML shell from cache.

**Implementation (`src/CallCenterTranscription.Web/Program.cs`):**
Placed immediately after the HSTS/exception handler block and before `app.UseRouting()`. The `OnStarting` callback fires just before the response starts writing, at which point `Content-Type` is already set by whichever endpoint handled the request. The callback gates on `ct.StartsWith("text/html", OrdinalIgnoreCase)` so:
- Razor Page responses (`text/html; charset=utf-8`) → headers set ✓
- Static asset responses (`text/css`, `application/javascript`, etc.) → headers NOT set ✓
- Health check (`/healthz`, `application/json`) → headers NOT set ✓

`MapStaticAssets()` in .NET 9 serves static files as endpoints via the routing system; `MapStaticAssets()` sets its own `Cache-Control: public, max-age=…, immutable` headers for fingerprinted assets. Because the `OnStarting` callback checks Content-Type at flush time, it never overwrites those headers.

**Middleware ordering preserved:** HSTS → html-no-cache middleware → UseRouting → UseAuthorization → /healthz → MapStaticAssets → MapRazorPages. No existing middleware was moved or removed.

---

# 2026-06-08T13:29:12.574-04:00 — Review: Lib assets + HTML cache fix

**Reviewer:** Athrun (Lead/Architect)  
**Verdict:** ✅ APPROVE

Lib provisioning paths match _Layout + _ValidationScriptsPartial exactly; libman restore step is in the build job before publish; SHA-pinned actions unchanged; cache middleware correctly scoped to text/html via OnStarting guard; middleware ordering preserved; build + publish verified 0 errors with all 5 assets at real sizes.

---

