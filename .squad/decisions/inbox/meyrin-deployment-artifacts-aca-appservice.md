# 2026-06-06T15:20:19.390-04:00 — Deployment artifact split (ACA API + App Service Web)

- **By:** Meyrin
- **Decision proposal:** For the current POC hosting direction, standardize deployment packaging as:
  1. **API (`CallCenterTranscription.Api`)** on **Azure Container Apps** via container image (Dockerfile required).
  2. **Web (`CallCenterTranscription.Web`)** on **Azure App Service** via source/package deploy (no Web Dockerfile required for now).
- **Why this matters to team:** This locks CI/CD and IaC shape for Lunamaria/Lacus integration points and avoids running two container supply chains when only API needs ACA.
- **Operational note:** API already has `/healthz`; Web currently has no health endpoint, so add one before enabling App Service Health Check.
- **Security follow-up (required):** `Security__RequireAuth=true` is not deployment-ready until the team chooses and implements a concrete auth model (for example, Entra-authenticated Web and JWT bearer validation on API/SignalR) plus corresponding app settings.
- **Source evidence:** `src/CallCenterTranscription.Api/Program.cs`, `src/CallCenterTranscription.Web/Program.cs`, `src/CallCenterTranscription.Web/Services/BackendApiOptions.cs`, `.squad/decisions.md`.
