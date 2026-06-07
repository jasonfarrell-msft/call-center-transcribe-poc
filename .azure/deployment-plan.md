# Azure Deployment Plan

> **Status:** Deployed

Generated: 2026-06-06T16:39:17-04:00

---

## Current Change Request: Frontend Mission Control Update

**Requested:** 2026-06-06T20:10:18.032-04:00

**Goal:** Deploy the first-pass call-center representative interface. The frontend should show a live-style call transcription view with diarization, ad hoc translation, a call sentiment indicator, and a mission-control health surface that can be continually expanded as Azure components and app features mature.

**Deployment Target:** Existing Azure deployment in `rg-callcentertranscribe-swc-mx01` / Sweden Central.

**POC authentication exception:** The user explicitly approved deploying this first-pass POC Web console/API unauthenticated for now. This exception applies only to the POC frontend update; service-to-service Azure access remains managed-identity based, and production/next hardening should add Entra ID user authentication before exposing real customer data.

**Status:** Deployed and verified.

### Planned Feature Scope

- [x] Call-center representative interface for active call monitoring.
- [x] Transcript timeline with speaker diarization.
- [x] Ad hoc translation visibility for non-English utterances.
- [x] Sentiment indicator for the current call.
- [x] Mission Control health panel for frontend, API, Azure AI/Speech/Translator/ACS readiness, and deployment status.
- [x] Preserve existing mock-first/demo-safe seams for features not yet backed by live ACS/media routes.

### Proposed Implementation Plan

**Release shape:** Existing AZD deployment, code-only feature update plus redeploy. No new Azure resources are required for the first pass.

**Mock vs. live boundary:**

| Area | First-pass behavior |
|------|---------------------|
| Web/API deployment | Live on existing App Service + Container Apps resources |
| Transcript feed | Scripted/mock session feed with stable event IDs and sequence ordering |
| Diarization | Speaker labels from mock/shared events; show `Speaker 1` / `Speaker 2` unless role is externally grounded |
| Ad hoc translation | Non-English utterances show a language badge and click-to-translate reveal; translation can be pre-seeded/cached for demo reliability |
| Sentiment | Display as a call/participant tone signal, not a diagnostic score; labels only, with trend when enough turns exist |
| Mission Control | Backend-summarized health/readiness panel; explicitly marks ACS/media and true AI streaming as mock/deferred until validated |

**Primary UI surfaces:**

- Header/connection strip: call state, mock/live badge, customer/call context, reconnect state.
- Transcript timeline: diarized utterance rows with timestamp, speaker chip, detected-language badge, confidence/detail text where useful.
- Translation reveal: per non-English utterance button with accessible label; show original text and translated text on demand.
- Sentiment card: non-color-only tone indicator such as `Calm`, `Upset`, `Cooling down`, plus trend/state copy.
- Mission Control panel: Web, API, SignalR/API stream, Speech, Translator, ACS, and AI/Foundry readiness with status/freshness/evidence.

**Likely code changes:**

| Project | Files / surfaces |
|---------|------------------|
| Web | `Pages/Index.cshtml`, `Pages/Index.cshtml.cs`, `Services/PipelineApiClient.cs`, `wwwroot/css/site.css`, optional `wwwroot/js/site.js`, optional Web view models |
| API | Existing `/api/events/*` routes, plus `GET /api/session/current` and `GET /api/mission-control/health` if not already present |
| Shared | Add/extend event DTOs for `callId`, `eventId`, `utteranceId`, correlation between transcript/translation, call sentiment summary, and mission-control health |
| Tests | Add DTO/API/UI smoke tests for transcript, translation CTA, sentiment rendering, mission-control health, and scaffold-text regression |

**Data contract decisions for this pass:**

- Transcript event should include `callId`, `eventId`, `sequence`, `utteranceId`, `timestampUtc`, `isFinal`, `speakerId`, `speakerDisplayLabel`, `speakerRole`/source if known, `text`, `detectedLanguage`, `confidence`, and `source`.
- Translation must correlate by `utteranceId` and/or `relatedTranscriptSequence`; `sequence` alone is not sufficient.
- Sentiment UI uses labels (`positive`, `neutral`, `mixed`, `negative` or rep-friendly labels) and trend (`improving`, `steady`, `worsening`), not exact percentages.
- Mission Control statuses must distinguish `healthy`, `degraded`, `offline`, `deferred`, and `mock`.

**Acceptance gates before redeploy:**

- Existing build/tests pass.
- Homepage no longer shows scaffold-only content.
- Transcript timeline renders ordered diarized turns.
- Translation CTA appears only for non-English utterances and reveals the translation on user action.
- Sentiment indicator is visible and non-color-only.
- Mission Control distinguishes live/healthy components from mocked/deferred components.
- API/Web `/healthz` remain HTTP 200 locally/deployed.
- After deployment, `azd show` endpoints are collected and Web/API health endpoints are rechecked.

**Deferred explicitly:**

- Supervisor whisper, CRM writeback, post-call QA summary, deep analytics dashboard.
- Live ACS callback/media/Event Grid route automation until API routes are implemented and validated.
- Production network hardening/private endpoints for runtime Key Vault access.

### Implementation Evidence

| Check | Result |
|-------|--------|
| Backend/shared mock contract | Implemented deterministic scripted session, transcript, translation, sentiment, and Mission Control API support |
| Frontend console | Implemented Razor rep console with transcript, ad hoc translation reveal, sentiment, and Mission Control |
| Review gate | QA approved after explicit degraded/disconnected states were added |
| Security gate | Approved with documented user-selected unauthenticated POC exception; no secrets added; service-to-service identity remains managed identity |
| Build/test | `dotnet build CallCenterTranscription.sln --nologo` pass; `dotnet test CallCenterTranscription.sln --no-build --nologo` pass (20/20) |

### Validation / Redeployment Checklist for This Update

- [x] Invoke `azure-validate`
- [x] Build/tests pass after final changes
- [x] Bicep compile/provision preview remains clean
- [x] AZD package succeeds
- [x] Plan status set to `Validated`
- [x] Invoke `azure-deploy`
- [x] Redeploy API and Web
- [x] Verify Web/API health endpoints
- [x] Verify frontend renders representative console

### Frontend Update Validation Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| AZD installation/auth/environment | `azd version && azd auth login --check-status && azd env get-values` | Pass; target remains `TSJasonFarrell-Sub`, `swedencentral`, `rg-callcentertranscribe-swc-mx01`; ACR endpoint and ACR-pull UAMI env values present | 2026-06-07T01:22:06Z |
| Build/tests/Bicep compile | `dotnet build CallCenterTranscription.sln --nologo && dotnet test CallCenterTranscription.sln --no-build --nologo && az bicep build --file infra/main.bicep --stdout` | Pass; 20/20 tests passed and Bicep compiled | 2026-06-07T01:22:06Z |
| AZD provision preview | `azd provision --preview --no-prompt` | Pass; preview generated successfully for existing resource group with no Azure changes applied | 2026-06-07T01:22:06Z |
| AZD package | `azd package --no-prompt` | Pass; API remote build package step and Web App package step completed | 2026-06-07T01:22:06Z |
| Static RBAC review | `grep -R "Microsoft.Authorization/roleAssignments\\|guid(.*principalId\\|principalId:" -n infra/*.bicep infra/modules/*.bicep` | Pass; ACR-pull UAMI and API runtime system MI role assignments remain scoped and principalId-seeded | 2026-06-07T01:22:06Z |
| Azure policy review | `az policy assignment list --scope /subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c --query "[].{name:name,displayName:displayName,enforcementMode:enforcementMode,scope:scope}" -o table` | Pass; no deployment-blocking policy identified in preflight output | 2026-06-07T01:22:06Z |

### Frontend Update Deployment Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Provision/update infrastructure | `azd provision --no-prompt` | Pass; no infrastructure changes to provision | 2026-06-07T01:26:48Z |
| Container Apps ACR RBAC gate | `az containerapp registry list ...` and `az role assignment list --scope <acrId> --assignee-object-id <uamiPrincipalId>` | Pass; Container App registry binding uses `uami-acrpull-cctrans-kdarok` and the UAMI has `AcrPull` | 2026-06-07T01:26:48Z |
| API deploy | `azd deploy api --no-prompt` | Pass; updated API image remote-built, pushed to ACR, and deployed to Container Apps | 2026-06-07T01:26:48Z |
| Web deploy | `azd deploy web --no-prompt` | Pass; updated Razor console package deployed to App Service | 2026-06-07T01:26:48Z |
| Endpoint discovery | `azd show` | Pass; API and Web endpoints returned | 2026-06-07T01:26:48Z |
| API health | `curl -fsS -i https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/healthz` | Pass; HTTP 200 with `{"status":"ok"}` | 2026-06-07T01:26:48Z |
| Web health | `curl -fsS -i https://web-cctrans-kdarok.azurewebsites.net/healthz` | Pass; HTTP 200 with `{"status":"ok"}` | 2026-06-07T01:26:48Z |
| Feature API endpoints | `curl` for `/api/session/current`, `/api/events/transcript`, `/api/events/translation`, `/api/events/sentiment`, `/api/mission-control/health` | Pass; scripted Maria Alvarez call, diarized transcript, Spanish translation, sentiment summary, and degraded/mock Mission Control returned | 2026-06-07T01:26:48Z |
| Web console content | `curl https://web-cctrans-kdarok.azurewebsites.net/` and grep for representative-console labels | Pass; page renders mock-feed status, Maria Alvarez context, transcript speaker turns, Spanish translation reveal, sentiment indicator, and Mission Control health; scaffold text absent | 2026-06-07T01:26:48Z |
| Live RBAC verification | `az role assignment list` on ACR, Key Vault, Speech, Translator, and AI Services scopes | Pass; ACR-pull UAMI has `AcrPull`; API system MI has `Key Vault Secrets User` and `Cognitive Services User` roles at resource scopes | 2026-06-07T01:26:48Z |

### Frontend Update Deployed Endpoints

| Service | Endpoint |
|---------|----------|
| API | https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/ |
| API health | https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/healthz |
| Web console | https://web-cctrans-kdarok.azurewebsites.net/ |
| Web health | https://web-cctrans-kdarok.azurewebsites.net/healthz |
| Azure Portal | https://portal.azure.com/#@/resource/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/overview |

---

## 1. Project Overview

**Goal:** Deploy the Azure components needed for the CallCenterTranscription POC according to established Squad decisions.

**Path:** Modernize Existing / Add Azure deployment support

**Target Resource Group:** `rg-callcentertranscribe-swc-mx01`

**Target Location:** `swedencentral` / Sweden Central for all region-bound resources.

**Important regional constraint:** Most resources can be deployed in Sweden Central. Azure Communication Services and Event Grid system topics are global-style services with regional data handling controls, and Translator is best deployed as a single-service `Global` resource because regional Translator endpoints do not support Entra authentication. These exceptions must still be placed in the same resource group.

---

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | POC |
| Scale | Small / live demo |
| Budget | Cost-optimized |
| Subscription | TSJasonFarrell-Sub (`bb4b2781-6739-4fa1-994e-4ad6ce55c59c`) |
| Location | Sweden Central (`swedencentral`) |
| Resource group | `rg-callcentertranscribe-swc-mx01` |
| Data residency preference | Sweden Central where supported; Europe/global exceptions only where Azure service shape requires it |
| Auth posture | Managed identity first; Key Vault only for unavoidable secrets; no secrets in source |
| Deployment posture | Prepare -> validate -> deploy via Azure skills; no direct deployment before approval |

---

## 3. Components Detected

| Component | Type | Technology | Path |
|-----------|------|------------|------|
| API | Backend API / SignalR hub | ASP.NET Core `net9.0` | `src/CallCenterTranscription.Api` |
| Web | Razor Pages frontend | ASP.NET Core `net9.0` | `src/CallCenterTranscription.Web` |
| Shared | Contracts / DTOs | .NET class library | `src/CallCenterTranscription.Shared` |
| AI | Reasoning seam | .NET class library | `src/CallCenterTranscription.Ai` |
| Telephony | Audio source seam | .NET class library | `src/CallCenterTranscription.Telephony` |
| Tests | Unit/smoke tests | xUnit / .NET | `tests/CallCenterTranscription.Tests` |

### Existing Azure Artifacts

| Artifact | Status |
|----------|--------|
| `azure.yaml` | Found |
| `infra/` | Present |
| Dockerfiles | Found: `src/CallCenterTranscription.Api/Dockerfile` |
| API health endpoint | Found: `/healthz` |
| Web health endpoint | Found: `/healthz` |

---

## 4. Recipe Selection

**Selected:** AZD (Bicep)

**Rationale:**

- This is an Azure-first multi-service POC with API + Web + supporting Azure services.
- At planning start, no existing Terraform, Bicep, AZD, or Docker deployment artifacts were present.
- AZD provides the simplest path for environment values and future `azure-validate` / `azure-deploy` handoff.
- Bicep keeps Azure resource definitions explicit while avoiding unnecessary Terraform state setup for this POC.

---

## 5. Architecture

**Stack:** API on Azure Container Apps, frontend on Azure App Service, Azure AI/ACS supporting services, mock-first app seams preserved.

### Service Mapping

| Component | Azure Service | SKU / Notes |
|-----------|---------------|-------------|
| API | Azure Container Apps | Consumption; external HTTPS/WSS ingress; target port `8080`; bootstrap probes are disabled by default (`enableApiHealthProbes=false`) until the real API image is deployed and `/healthz` is verified; identity split: system-assigned MI for runtime Azure access (Key Vault/Cognitive/ACS) + user-assigned MI dedicated to ACR pulls |
| Web | Azure App Service | Linux Basic B1 or equivalent cost-optimized SKU; source/package deploy; `/healthz` added; system-assigned managed identity |
| Container images | Azure Container Registry | Basic SKU; alphanumeric name; API image only |
| Telephony | Azure Communication Services | Same resource group; `dataLocation=Europe`; ACS resource floor only in this revision |
| Incoming call events | Event Grid system topic/subscription | **Deferred** until API ACS callback/media routes are implemented and validated |
| Speech / diarization | Azure AI Speech | Sweden Central; custom domain required for Entra-auth compatible Speech usage |
| Translation | Azure AI Translator | Single-service `Global` resource preferred for Entra-auth compatibility |
| Reasoning | Azure AI Foundry / Azure AI Services | Sweden Central regional project/deployment where available; start with cost-optimized model deployment behind `IReasoningClient` |
| Secrets | Azure Key Vault | Sweden Central; RBAC authorization; soft delete + purge protection; firewall `defaultAction=Deny` with `AzureServices` bypass |
| Logs | Log Analytics Workspace | Sweden Central |
| APM | Application Insights | Workspace-based, Sweden Central |

### Deploy Now

| Resource | Purpose |
|----------|---------|
| Resource group | Existing or create `rg-callcentertranscribe-swc-mx01` in Sweden Central |
| Log Analytics Workspace | Centralized ACA/App Service/Application Insights logs |
| Application Insights | Application telemetry |
| Key Vault | Storage for unavoidable secrets only; RBAC mode; firewall deny-by-default |
| Azure Container Registry | API image storage |
| Container Apps Environment | API hosting environment |
| Container App for API | Public HTTPS/WSS ingress with safe bootstrap defaults (probes off until real API image is deployed) |
| App Service plan + Web App | Rep-facing Razor UI |
| Azure Communication Services | Real-call resource floor only; data location Europe |
| Azure AI Speech | Real-time STT/diarization/language detection |
| Azure AI Translator | Non-English text translation |
| Azure AI Foundry / AI Services project/deployment | Reasoning via `IReasoningClient` |

### Defer

| Resource | Reason |
|----------|--------|
| Event Grid system topic/subscription for ACS `IncomingCall` | Deferred until API ACS incoming-call callback and media streaming routes are implemented and validated |
| Live ACS callback/media automation enablement | Deferred; this POC revision keeps mock seams as the reliable demo path |
| Azure AI Search | RAG data is mocked for initial POC |
| Cosmos DB / SQL / Storage data plane | No persistence requirement yet |
| Redis | No scale-out cache requirement yet |
| Azure SignalR / Web PubSub | In-process SignalR is enough for POC |
| Private endpoints / VNet integration | Required before production-grade Key Vault/data-plane lockdown for runtime traffic |
| Provisioned model throughput | Cost-optimized POC starts serverless/shared where available |
| Custom Speech / Document Translation / Content Safety | Outside current demo floor |

### Required App Settings

| App | Setting | Purpose |
|-----|---------|---------|
| API | `Security__RequireAuth` | Keep `false` until explicit auth config exists; set `true` only after auth implementation |
| API | `Speech__Endpoint` | Speech endpoint |
| API | `Speech__CandidateLanguages` | Language detection candidates |
| API | `Translator__Endpoint` | Translator endpoint |
| API | `Foundry__Endpoint` | Foundry / AI Services endpoint |
| API | `Foundry__DeploymentName` | Reasoning deployment |
| API | `Acs__Endpoint` | ACS endpoint (resource floor only; live callback/media routes deferred) |
| Web | `BackendApi__BaseUrl` | API base URL from deployed Container App |

---

## 6. Security, Reliability, and Reviewer Gates

Security reviewer follow-up returned **REJECT** on deployment readiness. The following controls are now mandatory before validation/deployment:

| Gate | Required Action |
|------|-----------------|
| Managed identity | API uses dual identities (system-assigned for runtime + user-assigned for ACR pull); Web uses system-assigned managed identity |
| RBAC | API runtime system identity gets least-privilege Azure AI / ACS / Key Vault roles; AcrPull is granted only to the API ACR-pull user-assigned identity at ACR scope |
| Key Vault | RBAC authorization, soft delete, purge protection; no hardcoded secrets |
| Key Vault network | Keep `publicNetworkAccess=Enabled` for POC bootstrap but set firewall `defaultAction=Deny` and `bypass=AzureServices`; require network integration/private endpoints before production runtime secret access |
| Auth posture | Do not set `Security__RequireAuth=true` until explicit auth settings are present |
| Forwarded headers trust | **Resolved:** Removed trust-all forwarded-header configuration from API/Web startup (`KnownNetworks`/`KnownProxies` are no longer cleared). Rely on Azure edge HTTPS enforcement (`allowInsecure=false` for Container Apps and `httpsOnly=true` for App Service). |
| Web health | Web exposes `/healthz`; keep it configured for App Service health checks |
| API health bootstrap | Keep ACA target port `8080`; defer liveness/readiness probes until the real API image is deployed and `/healthz` is verified |
| ACS live automation | Defer Event Grid + callback/media automation until API ACS routes are implemented and validated; do not claim live readiness yet |
| Demo fallback | Preserve mock audio / mock reasoning fallback for live demo reliability |
| Smoke checks | Post-deploy checks must hit Web `/healthz`, API `/healthz`, SignalR/API seams, and ACS callback/media routes only after they are implemented |

---

## 7. Provisioning Limit Checklist

**Purpose:** Validate that the selected subscription and region have sufficient quota/capacity for all resources to be deployed.

Quota CLI was attempted after installing the quota extension and registering `Microsoft.Quota`. `Microsoft.App` and `Microsoft.Web` returned limited quota data; several providers returned `BadRequest`, so current regional resource counts plus official service-limit fallback are used where quota CLI is unsupported.

| Resource Type | Number to Deploy | Current in Sweden Central | Total After Deployment | Limit/Quota | Notes |
|---------------|------------------|---------------------------|------------------------|-------------|-------|
| `Microsoft.App/managedEnvironments` | 1 | 0 | 1 | 50 | Fetched from `az quota list`: `Managed Environment Count` |
| `Microsoft.App/containerApps` | 1 | 0 | 1 | 200 apps per environment | Fallback: Container Apps service limits |
| `Microsoft.Web/serverfarms` | 1 | 0 | 1 | 100 Basic/Standard/Premium plans per region | Fallback: App Service service limits; quota CLI returned non-actionable `Total Regional VMs=0` |
| `Microsoft.Web/sites` | 1 | 0 | 1 | App Service subscription limits; well below regional app limits | Fallback: current count + official service limits |
| `Microsoft.ContainerRegistry/registries` | 1 | 0 | 1 | 100 registries per region | Quota CLI unsupported (`BadRequest`); fallback service limit |
| `Microsoft.OperationalInsights/workspaces` | 1 | 1 | 2 | 1,000 workspaces per subscription | Quota CLI unsupported (`BadRequest`); fallback service limit |
| `Microsoft.Insights/components` | 1 | 0 | 1 | Application Insights resource limits; below practical subscription limits | Quota CLI unsupported (`BadRequest`); fallback service limit |
| `Microsoft.KeyVault/vaults` | 1 | 0 | 1 | 500 vaults per region | Quota CLI unsupported (`BadRequest`); fallback service limit |
| `Microsoft.CognitiveServices/accounts` | 3 | 5 | 8 | 200 accounts per region | Quota CLI unsupported (`BadRequest`); fallback service limit; accounts cover Speech, Translator, Foundry/AI Services |
| `Microsoft.Communication/CommunicationServices` | 1 | 0 | 1 | 50 resources per region / subscription class | Provider currently not registered; fallback service limit |
| `Microsoft.EventGrid/systemTopics` / subscriptions | 0 | 0 | 0 | Event Grid subscription limits; below practical limits | Deferred in this revision until API ACS callback/media route implementation is complete |

**Status:** All planned resources are within known limits based on quota CLI where available and fallback service-limit checks where quota CLI is unsupported.

---

## 8. Execution Checklist

### Phase 1: Planning

- [x] Analyze workspace
- [x] Gather requirements
- [x] Confirm subscription and location with user
- [x] Prepare resource inventory
- [x] Fetch quotas and validate capacity
- [x] Scan codebase
- [x] Select recipe
- [x] Plan architecture
- [x] User approved this plan

### Phase 2: Execution

- [x] Research components
- [x] Add Web `/healthz` endpoint
- [x] Generate `azure.yaml`
- [x] Generate API Dockerfile
- [x] Generate Bicep infrastructure under `infra/`
- [x] Make ACA bootstrap safe by default (`enableApiHealthProbes=false`; keep target port `8080`)
- [x] Set Key Vault firewall to deny-by-default with `AzureServices` bypass for POC bootstrap
- [x] Remove trust-all forwarded-header startup configuration; rely on Azure edge HTTPS enforcement (`allowInsecure=false`, `httpsOnly=true`)
- [x] Defer ACS live callback/media/Event Grid readiness claims until API routes are implemented
- [x] Configure AZD environment values for confirmed subscription and location
- [x] Configure AZD remote container build for API (`docker.remoteBuild=true`)
- [x] Add secure app settings and outputs
- [x] Keep `Security__RequireAuth=false` until auth config exists
- [x] Run local build/tests
- [x] Compile `infra/main.bicep` via `az bicep build --file infra/main.bicep --stdout`
- [x] Split ACA identity model: user-assigned MI for ACR pulls + system-assigned MI for runtime RBAC
- [x] Update plan status to `Ready for Re-Validation`

### Phase 3: Validation

- [x] Re-run `azure-validate` after identity/RBAC role-assignment idempotency changes
- [x] Invoke `azure-validate`
- [x] All validation checks pass
  - [x] AZD installation
  - [x] Azure YAML schema/config inspection
  - [x] AZD environment setup
  - [x] Authentication check
  - [x] Subscription/location/resource group check
  - [x] Provision preview
  - [x] Build verification
  - [x] ACR remote build configuration validation (`docker.remoteBuild=true`)
  - [x] Package validation
  - [x] Bicep compilation
  - [x] Static RBAC role verification
  - [x] Azure Policy validation
- [x] Update plan status to `Validated`
- [x] Record validation proof below

### Phase 4: Deployment

- [x] Invoke `azure-deploy`
- [x] Deployment successful
- [x] Report deployed endpoint URLs
- [x] Update plan status to `Deployed`

---

## 9. Validation Proof

> AZD package validation for the API now uses Azure Container Registry remote build (`services.api.docker.remoteBuild=true`), which removes the local Docker/Podman runtime dependency.

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Local build | `dotnet build CallCenterTranscription.sln --nologo` | Pass | 2026-06-06T16:39:17-04:00 |
| Local tests | `dotnet test CallCenterTranscription.sln --no-build --nologo` | Pass (4/4) | 2026-06-06T16:39:17-04:00 |
| Infra compile | `az bicep build --file infra/main.bicep --stdout` | Pass | 2026-06-06T16:39:17-04:00 |
| Identity/RBAC idempotency update | ACA runtime role-assignment GUID seeds now include system MI principalId; ACR-pull module GUID seed now uses principalId | Pass; security reviewer approved | 2026-06-06T20:41:37Z |
| AZD installation | `azd version` | Pass; azd 1.25.2 installed | 2026-06-06T20:41:37Z |
| Azure YAML schema/config inspection | `test -f azure.yaml && test -d infra && grep -n "remoteBuild: true\\|host: containerapp\\|host: appservice" azure.yaml` | Pass; AZD config and infra folder present; API Container App, Web App Service, API remote build configured | 2026-06-06T20:41:37Z |
| Aspire pre/post provisioning applicability | `find . -name '*.AppHost.csproj' ...; grep -R "Aspire.Hosting" --include='*.csproj' .` | Pass; no Aspire AppHost or Aspire.Hosting package references detected | 2026-06-06T20:41:37Z |
| AZD environment | `azd env get-values` | Pass; subscription, location, and resource group pinned | 2026-06-06T15:18:26.672-04:00 |
| AZD authentication | `azd auth login --check-status` | Pass; logged in to Azure | 2026-06-06T15:18:26.672-04:00 |
| AZD provision preview | `azd provision --preview --no-prompt` | Pass; generated preview for target RG with no Azure changes applied; updates ACA identity from system-only to system + ACR-pull UAMI and removes stale system registry binding for placeholder image | 2026-06-06T20:41:37Z |
| Docker build context validation | `grep -R "npm ci" -n src/CallCenterTranscription.Api/Dockerfile azure.yaml .dockerignore` | Pass; no `npm ci`/package-lock dependency; API remote build enabled | 2026-06-06T20:41:37Z |
| AZD package | `azd package --no-prompt` | Pass; packaged successfully with ACR remote build enabled for API (no local Docker/Podman runtime error) | 2026-06-06T20:07:42Z |
| Static RBAC review | `grep -R "Microsoft.Authorization/roleAssignments\\|guid(.*principalId\\|principalId:" -n infra/*.bicep infra/modules/*.bicep` | Pass; role assignment GUID seeds include scope id, principalId, and roleDefinitionId; API system MI scoped to runtime services; ACR-pull UAMI scoped to AcrPull | 2026-06-06T20:41:37Z |
| Azure policy review | `az policy assignment list --scope /subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c --query "[].{name:name,displayName:displayName,enforcementMode:enforcementMode,scope:scope}" -o table` | Pass; no deployment-blocking policy found in preflight output | 2026-06-06T20:41:37Z |
| Azure validation gate (post-change) | `azure-validate` | Pass | 2026-06-06T20:41:37Z |
| ACR registry binding recovery | `azd deploy api --no-prompt` then `az containerapp registry list ...` | Initial deploy failed after remote build because ACA had no ACR registry binding; root cause fixed by always binding deployment ACR to ACR-pull UAMI | 2026-06-06T20:56:09Z |
| Recovery security review | Security reviewer review of unconditional UAMI ACR registry binding | Pass; preserves UAMI-for-ACR-pull and system-MI-for-runtime split with no secrets/admin credentials | 2026-06-06T20:56:09Z |
| Recovery build/test/Bicep/preview | `dotnet build CallCenterTranscription.sln --nologo && dotnet test CallCenterTranscription.sln --no-build --nologo && az bicep build --file infra/main.bicep --stdout && azd provision --preview --no-prompt` | Pass; preview adds ACR registry binding using `acrPullUserAssignedIdentity.id` | 2026-06-06T20:56:09Z |

**Validated by:** GitHub Copilot CLI via `azure-validate`
**Validation timestamp:** 2026-06-06T20:56:09Z

---

## 10. Deployment Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Provision infrastructure | `azd provision --no-prompt` | Pass; existing resources updated in `rg-callcentertranscribe-swc-mx01`; Container App recovered from failed state | 2026-06-06T20:53:00Z |
| ACR registry binding provision | `azd provision --no-prompt` | Pass; Container App registry binding added for `acrcctranskdarok.azurecr.io` using `uami-acrpull-cctrans-kdarok` | 2026-06-06T20:58:00Z |
| Container Apps + ACR RBAC health gate | `az containerapp registry list ...` and `az role assignment list --scope <acrId> --assignee-object-id <uamiPrincipalId>` | Pass; registry uses UAMI, no username/password secret, and UAMI has `AcrPull` on ACR | 2026-06-06T20:58:00Z |
| API deploy | `azd deploy api --no-prompt` | Pass; remote build pushed image and updated Container App revision | 2026-06-06T20:59:00Z |
| Web deploy | `azd deploy web --no-prompt` | Pass; App Service package uploaded and runtime started | 2026-06-06T21:00:00Z |
| Endpoint discovery | `azd show` | Pass; API and Web endpoints returned | 2026-06-06T21:00:56Z |
| API health check | `curl -fsS -i https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/healthz` | Pass; HTTP 200 with `{"status":"ok"}` | 2026-06-06T21:00:56Z |
| Web health check | `curl -fsS -i https://web-cctrans-kdarok.azurewebsites.net/healthz` | Pass; HTTP 200 with `{"status":"ok"}` | 2026-06-06T21:00:56Z |
| Live role verification | `az role assignment list` on ACR, Key Vault, Speech, Translator, and AI Services scopes | Pass; ACR-pull UAMI has `AcrPull`; API system MI has `Key Vault Secrets User` and `Cognitive Services User` roles at resource scopes | 2026-06-06T21:00:56Z |

### Deployed Endpoints

| Service | Endpoint |
|---------|----------|
| API | https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/ |
| API health | https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/healthz |
| Web | https://web-cctrans-kdarok.azurewebsites.net/ |
| Web health | https://web-cctrans-kdarok.azurewebsites.net/healthz |
| Azure Portal | https://portal.azure.com/#@/resource/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/overview |

### Live Role Verification

- `uami-acrpull-cctrans-kdarok` (`50651228-cfbd-43a4-986b-310a08df2af1`) -> `AcrPull` on `acrcctranskdarok`.
- API system-assigned identity (`6edcf409-903a-49ec-ae48-aed391da1fa7`) -> `Key Vault Secrets User` on `kv-cctrans-kdarok`.
- API system-assigned identity (`6edcf409-903a-49ec-ae48-aed391da1fa7`) -> `Cognitive Services User` on `speech-cctrans-kdarok`, `translator-cctrans-kdarok`, and `ai-cctrans-kdarok`.

---

## 11. Files to Generate or Modify

| File | Purpose | Status |
|------|---------|--------|
| `.azure/deployment-plan.md` | Revision-cycle deployment readiness plan | Updated |
| `azure.yaml` | AZD configuration | Updated (enabled `services.api.docker.remoteBuild=true` for ACR remote build packaging) |
| `.dockerignore` | Build context hygiene | Reviewed (no change required in this revision) |
| `infra/main.bicep` | Main Bicep entrypoint | Updated (runtime role-assignment GUID seeds use system MI principalId; ACA ACR registry binding is preconfigured with the ACR-pull UAMI for AZD image revisions) |
| `infra/main.parameters.json` | Bicep deployment parameters | Updated (`enableApiHealthProbes=false`) |
| `infra/modules/acr-pull-role-assignment.bicep` | Scoped role-assignment module (ACR + runtime RBAC scopes) | Updated (role-assignment GUID seed now uses principalId; principalName parameter removed) |
| `src/CallCenterTranscription.Api/Dockerfile` | API container image | Reviewed (no change required) |
| `src/CallCenterTranscription.Api/Program.cs` | API route surface | Updated (removed trust-all forwarded-header config and app-level HTTPS redirection; `/healthz` unchanged) |
| `src/CallCenterTranscription.Web/Program.cs` | Web host startup and health endpoint | Updated (removed trust-all forwarded-header config and app-level HTTPS redirection; `/healthz` unchanged) |

---

## 12. Next Steps

> Current: Azure resources and app services are deployed and healthy in `rg-callcentertranscribe-swc-mx01`.

1. Set `enableApiHealthProbes=true` and re-apply infra after the team is ready to enforce ACA liveness/readiness probes against the deployed API `/healthz`.
2. Implement API ACS incoming-call callback + media stream routes, then add Event Grid system topic/subscription and re-run validation.
3. Before production hardening, add network integration/private endpoint strategy for runtime Key Vault access if secrets are consumed at runtime.
