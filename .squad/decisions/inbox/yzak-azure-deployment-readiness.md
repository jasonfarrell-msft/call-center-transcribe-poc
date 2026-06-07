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
