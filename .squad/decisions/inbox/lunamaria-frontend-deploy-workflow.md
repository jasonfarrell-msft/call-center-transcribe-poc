# 2026-06-07T06:29:29.980-04:00 — Frontend-only App Service deploy workflow

- **By:** Lunamaria
- **Decision proposal:** Standardize frontend-only deployment on `.github/workflows/deploy-frontend.yml` with push-to-`main` path filters and manual dispatch. Build/publish only `src/CallCenterTranscription.Web/CallCenterTranscription.Web.csproj`, then deploy the artifact to the existing App Service using GitHub OIDC federation and repo-scoped Azure identifiers instead of hardcoded values.
- **Why this matters to team:** It lets UI-only changes ship without touching ACA/API resources, keeps the frontend pipeline small and fast, and reduces accidental infrastructure churn while backend deployment remains separate.
- **Operational note:** The workflow verifies the target App Service exists in the configured resource group before deployment. It expects `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` repository secrets, plus `AZURE_WEBAPP_NAME` and `AZURE_RESOURCE_GROUP` repository variables.
- **Source evidence:** `azure.yaml`, `src/CallCenterTranscription.Web/CallCenterTranscription.Web.csproj`, `infra/main.bicep`, `README.md`, `.github/workflows/deploy-frontend.yml`
