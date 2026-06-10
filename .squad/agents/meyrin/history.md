# Meyrin — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** ACS audio fork (Call Automation / media streaming), real-time WebSocket ingestion, backend APIs feeding the UI, and a swappable mock/scripted audio source for the demo.
- **Constraints:** POC may be scripted; must be able to run on mock audio. Managed identity, no secrets in code. Latest GA Azure SDKs.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

- **2026-06-08T15:24:21.856-04:00 — Speech env vars added to ACA Bicep (Speech__Region + Speech__ResourceId):**
  - Added `Speech__Region` (value: `speechAccount.location` → `'swedencentral'`) and `Speech__ResourceId` (value: `speechAccount.id`) to the ACA container `env` array in `infra/main.bicep`, placed after `Speech__CandidateLanguages` and before `Translator__Endpoint`, consistent style with existing env block.
  - These are **non-secret** values: a region string and an ARM resource ID — no keys, no secrets.
  - Resolved Speech ARM ID (live): `/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-callcentertranscribe-swc-mx01/providers/Microsoft.CognitiveServices/accounts/speech-cctrans-kdarok`
  - These unblock managed-identity Speech auth: the consumer's startup guard requires `Speech:Region` to be non-empty, and `Speech:ResourceId` is required to format the AAD token as `aad#{resourceId}#{token}` for a custom-subdomain Speech resource.
  - Consumer config keys confirmed (READ-ONLY review of `SpeechTranscriptionService.cs`): `Speech:Endpoint` / `Speech:Region` / `Speech:ResourceId` — map exactly to env vars `Speech__Endpoint` / `Speech__Region` / `Speech__ResourceId`.
  - `az bicep build infra/main.bicep` → **0 errors, 0 warnings**.
  - Updated deploy recipe (Step 6) in `meyrin-acs-eventgrid-and-deploy.md` with corrected flip command including both new Speech vars.

- **2026-06-08T15:24:21.856-04:00 — Event Grid IncomingCall wiring, Speech RBAC verification, API/ACA deploy recipe:**

  **Event Grid System Topic + Subscription shape (added to `infra/main.bicep`):**
  - System Topic: `Microsoft.EventGrid/systemTopics@2022-06-15`, `location: 'global'` (required for ACS), `topicType: 'Microsoft.Communication.CommunicationServices'`, `source: communicationService.id`. Name var: `evgt-acs-${uniqueSuffix}`.
  - Event Subscription: `Microsoft.EventGrid/systemTopics/eventSubscriptions@2022-06-15`, child of the system topic, endpoint `${apiBaseUrl}/api/events/acs/incoming-call`, filter `Microsoft.Communication.IncomingCall`, `EventGridSchema`, retry 30 attempts / 1440 min TTL, no dead-letter.
  - Delivery auth: plain webhook per Athrun go-live sign-off. Entra delivery auth deferred for POC.
  - **Key constraint:** The subscription resource fires a `SubscriptionValidationEvent` handshake on creation. A `azd provision` against an ACA running only the placeholder image will fail this handshake — the subscription must be created surgically AFTER the API webhook image is deployed. The system topic creation is safe anytime.
  - Bicep build: 0 errors, 0 warnings.

  **Live Speech RBAC status:**
  - Verified `Cognitive Services User` (GUID `a97b65f3-24c7-4388-baec-2e87135dc908`) is **already assigned live** on `speech-cctrans-kdarok` scoped to ACA system MI `6edcf409-903a-49ec-ae48-aed391da1fa7`. GUID confirmed in directory. No surgical fix required.

  **API/ACA deploy recipe (the key unblocker):**
  - No API CI/CD workflow exists — only `deploy-frontend.yml` (Web/App Service). Deploy path is manual `az` commands.
  - `azure.yaml` declares `remoteBuild: true` for the api service — ACR Tasks can build the image without local Docker.
  - **Safe recipe:** `az acr build --registry acrcctranskdarok --image api:<tag> --file src/CallCenterTranscription.Api/Dockerfile .` (from repo root) → `az containerapp update --name ca-api-cctrans-kdarok --resource-group rg-callcentertranscribe-swc-mx01 --image acrcctranskdarok.azurecr.io/api:<tag>`.
  - After deploy, verify `/healthz` returns 200 before creating the Event Grid subscription.
  - `azd deploy api` is technically possible but requires reconstructing the bare `.azure/` env (only `AZURE_ENV_NAME` set) — risky and not recommended per Athrun.

  **Pending live steps (ordered):**
  1. Lacus merges `SpeechTranscriptionService` + `DemoSafety` guard removal → API image ready
  2. `az acr build` → `az containerapp update` (image deploy)
  3. `curl /healthz` confirms webhook live
  4. `az eventgrid system-topic create` (topic only, safe)
  5. `az eventgrid system-topic event-subscription create` (triggers handshake — must be AFTER step 3)
  6. Coordinator flips `minReplicas=1` + `AudioSource__Mode=Acs` via `az containerapp update`
  7. Dyakka end-to-end call test + demo runbook

- **2026-06-08T15:05:37-04:00 — ACS RBAC role GUID correction (infra/main.bicep):**
  - **Problem:** The variable `communicationServicesContributorRoleDefinitionId` referenced GUID `2b4609a5-7812-4aba-b5e3-076e6a078419` ("Communication Services Contributor"), which does not exist in the target directory (`TSJasonFarrell-Sub`). A future `azd provision` would have failed with `RoleDefinitionDoesNotExist`.
  - **Fix — Location 1 (line ~82–89, variable definition):** Renamed var to `communicationServiceOwnerRoleDefinitionId`; updated GUID to `09976791-48a7-449e-bb21-39d1a415f350`; updated comment to reflect "Communication and Email Service Owner" (only available Communication built-in role in this directory; broader than ideal but resource-scoped, POC-acceptable; reassess if Microsoft ships a narrower ACS data-plane role).
  - **Fix — Location 2 (line ~460, role assignment params):** Updated `roleDefinitionId` reference from `communicationServicesContributorRoleDefinitionId` → `communicationServiceOwnerRoleDefinitionId`. Scope, principal, guid() naming, and principalType all unchanged.
  - **Verification:** `grep` confirms old GUID/var absent and new GUID/var present in both spots. `az bicep build --file infra/main.bicep` → 0 errors, 0 warnings.
  - **Impact:** Unblocks future `azd provision` — the role assignment will now resolve successfully against the subscription's actual role catalog.

- **2026-06-08T14:49:06.749-04:00 — ACS dataLocation flipped Europe → United States:**
  - **Authoritative value lives in two places:** param default in `infra/main.bicep` (line ~13) AND the `communicationDataLocation.value` in `infra/main.parameters.json`. The parameters file is what actually wins at `azd provision` time — both must be updated together.
  - **`dataLocation` is IMMUTABLE** — ARM rejects in-place updates. Switching regions requires manually deleting the existing ACS resource before running `azd provision`. Now is the safe time (no number purchased, no Event Grid wired, no data at risk).
  - **RBAC unaffected:** `apiToAcsRoleAssignment` uses `guid(communicationServicesAccount.id, principalId, roleDefinitionId)` — deterministic name re-computed against the new resource id on reprovision. Role assignment re-applies automatically in the same provision run.
  - **Env var unaffected:** `AudioSource__Mode = 'Mock'` stays; flip to `'Acs'` deferred until phone number + Event Grid are wired.
  - **Operator steps (Jason runs):** delete existing ACS resource → `azd provision` → portal acquire US toll-free → (next round) Event Grid + Entra delivery auth → flip `AudioSource__Mode=Acs`.

- **2026-06-08T13:29:12.574-04:00 — /lib static-asset provisioning (libman) + HTML no-cache middleware:**

  **How /lib is provisioned:**
  - `libman.json` lives at `src/CallCenterTranscription.Web/libman.json`. Provider: `jsdelivr`. Four libraries pinned: `bootstrap@5.3.3`, `jquery@3.7.1`, `jquery-validation@1.21.0`, `jquery-validation-unobtrusive@4.0.0`. They restore into `wwwroot/lib/{name}/dist/…`.
  - `dotnet-tools.json` at `.config/dotnet-tools.json` pins `microsoft.web.librarymanager.cli@3.0.71`.
  - Deploy workflow (`deploy-frontend.yml`) runs `dotnet tool restore && dotnet tool run libman restore` (working-directory: `src/CallCenterTranscription.Web`) between the NuGet restore and `dotnet publish` steps. This means every CI build pulls fresh lib assets from jsdelivr, and `dotnet publish` includes them — no vendor blobs committed to git.
  - KEY: `libman restore` must run BEFORE `dotnet publish`. The workflow step is a plain `run:` step; no new SHA-pinned action required.

  **HTML no-cache middleware (Program.cs):**
  - Added inline `app.Use` middleware placed AFTER the HSTS/exception-handler block and BEFORE `app.UseRouting()`.
  - Uses `context.Response.OnStarting(…)` to check Content-Type at flush time. Only fires `Cache-Control: no-cache, no-store, must-revalidate` + `Pragma: no-cache` + `Expires: 0` when Content-Type starts with `"text/html"`.
  - Static asset responses (served by `MapStaticAssets()` endpoint — Content-Type: text/css, application/javascript, etc.) are NOT affected; their `Cache-Control: public, max-age=…, immutable` fingerprint headers remain intact.
  - Health check (`/healthz`, application/json) and any future non-HTML endpoints are also unaffected.
  - Middleware order preserved: HSTS → html-no-cache → UseRouting → UseAuthorization → /healthz → MapStaticAssets → MapRazorPages.

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
- **2026-06-08T14:05:26.535-04:00 — ACS Option C Bicep infra (RBAC, minReplicas, AudioSource__Mode env var):**

  **ACS RBAC role assignment:**
  - Role: `Communication Services Contributor` (GUID `2b4609a5-7812-4aba-b5e3-076e6a078419`).
  - Scope: the single `Microsoft.Communication/communicationServices` resource (not RG/sub).
  - Principal: `apiContainerApp.identity.principalId` — the ACA Container App **system-assigned** managed identity.
  - Pattern: extended `modules/acr-pull-role-assignment.bicep` with a `'communicationServices'` scopeType following the identical `cognitiveServices` branch pattern — existing resource reference + conditional `Microsoft.Authorization/roleAssignments@2022-04-01` with `name: guid(communicationServicesAccount.id, principalId, roleDefinitionId)`, `principalType: 'ServicePrincipal'`, `scope: communicationServicesAccount`.
  - Called from `main.bicep` as `module apiToAcsRoleAssignment` after the other role-assignment modules.
  - Idempotent: deterministic `guid()` name means re-deploying is safe.
  - Justification captured in comments: no narrower built-in covers Call Automation AnswerCall + StartMediaStreaming; resource-scoped + system MI mitigates the broad-ish role for the POC.

  **minReplicas param:**
  - Added `param apiMinReplicas int = 1` (with description per Athrun's spec).
  - Wired into `scale.minReplicas` of the ACA Container App (`minReplicas: apiMinReplicas`).
  - Was `minReplicas: 0` (hardcoded); now param-driven, default 1. `maxReplicas` confirmed at 1 — left unchanged.
  - `main.parameters.json` updated with `"apiMinReplicas": { "value": 1 }`.

  **AudioSource__Mode env var:**
  - Added `AudioSource__Mode = 'Mock'` to the ACA container `env` array (after `Acs__Endpoint`).
  - Comment explains: flip to `'Acs'` after ACS phone number + Event Grid subscription provisioned — no rebuild required (Dyakka reads `AudioSource:Mode` via `IConfiguration`; double-underscore maps to colon-separated key).

  **Bicep build:** `az bicep build infra/main.bicep` — **0 errors, 0 warnings**.
 Lunamaria's nav-toggle removal accidentally deleted the `const translationButton` declaration in site.js click handler, causing ReferenceError on every translation toggle click. Fixed by restoring the missing const line and realigning `case "transcript-scroller":` indentation in `restoreFocus`. Both `node --check` and `dotnet build` pass clean. Approved on Athrun re-gate.

## 2026-06-08 — Bicep ACS RBAC GUID Fix
**Status:** COMPLETED & COMMITTED

Updated infra/main.bicep to reflect corrected ACS RBAC role:

**Changes:**
- Renamed var: `communicationServicesContributorRoleDefinitionId` → `communicationServiceOwnerRoleDefinitionId`
- Updated GUID: `2b4609a5-7812-4aba-b5e3-076e6a078419` → `09976791-48a7-449e-bb21-39d1a415f350`
- Updated references & comments to match corrected role name
- bicep build validation: 0 errors

Committed to main. Aligns with athrun's RBAC decision revision (role unavailable in directory, switched to available alternative).

Next: Coordinate with Lacus on Event Grid + audio consumer design.

## 2026-06-08T19:24:21Z — Orchestration Logs & Session Completion

**Decisions committed to decisions.md:**
1. `athrun-acs-go-live-signoff` — Architecture sign-off + 8-step go-live sequence + guardrails
2. `athrun-go-live-build-review` — Gate review (REQUEST CHANGES: Speech__Region + Speech__ResourceId env vars) → FIXED by Meyrin
3. `lacus-speech-consumer-built` — SpeechTranscriptionService consumer (commit 7426ebe)
4. `meyrin-acs-eventgrid-and-deploy` — Event Grid Bicep + deploy recipe (commit 9f28cdd)
5. `meyrin-speech-env-vars-fix` — Speech env vars added to ACA Bicep (commit 4decb78)

**Orchestration logs created:**
- `.squad/orchestration-log/2026-06-08T19-24-21Z-meyrin-1.md` (Event Grid + RBAC + deploy recipe)
- `.squad/orchestration-log/2026-06-08T19-24-21Z-meyrin-2.md` (Speech env vars fix)

**Session log created:**
- `.squad/log/2026-06-08T19-24-21Z-acs-go-live-build.md` (PENDING: 6-step go-live sequence + fallback)

**Inbox files merged & deleted:**
- 6 inbox files merged into decisions.md (120583 → 131795 bytes)
- `.squad/decisions/inbox/` cleared

**All .squad/ files committed to git** (staged via surgical `git add` per policy).

## 2026-06-10 — Rep Call-Control: `repAccepted` Event (Task 3)

**Athrun + Yzak decision:** Rep call-control feature incoming. **Meyrin owns Task 3** (Dyakka owns Task 4 on same file — rebases after Meyrin).

**Task 3 — Backend: `repAccepted` event broadcast**
- **Owner:** Meyrin
- **Description:** In `HandleCallbacksAsync`, on `AddParticipantSucceeded`, broadcast a new `repAccepted` SignalR event (on `PipelineContract.StreamNames` group) with the callId. This is the gate for the frontend to begin showing transcript lines.
- **Files:** `src/CallCenterTranscription.Api/AcsEndpoints.cs` (callbacks section), `src/CallCenterTranscription.Api/Hubs/PipelineContract.cs`
- **Dependencies:** None (additive change to existing callback handler).
- **Build validation:** `dotnet build` → 0 errors

**Merge order:** Task 3 (Meyrin) **first** (small, additive) → Task 4 (Dyakka rebases on same file).

**Frontend dependency:** Lunamaria (Task 1+5) waits for this event to exist before gating transcript rendering on receipt.

**Key insight:** This is the mechanism that differentiates "Call Pending" (ringing, rep hasn't accepted yet) from "Connected" (rep accepted, transcript visible).
