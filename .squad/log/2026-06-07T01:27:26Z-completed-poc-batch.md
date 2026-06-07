# Completed POC Batch

- **Timestamp (UTC):** 2026-06-07T01:27:26Z
- **Scope:** First-pass call-center representative frontend and backend/API mock session delivery.
- **Features shipped:** transcript timeline with diarization, Spanish ad hoc translation reveal, sentiment indicator, Mission Control health panel, mock session endpoints.
- **Deployment/validation:** Build/tests 20/20, `azd` preview/package, API/Web deploy, health checks, feature endpoint checks, Web console content check, live RBAC verification.
- **Deployment targets:** `https://ca-api-cctrans-kdarok.gentlegrass-79ff7e16.swedencentral.azurecontainerapps.io/` and `https://web-cctrans-kdarok.azurewebsites.net/`
- **Auth note:** Unauthenticated POC deployment exception explicitly approved; production/next hardening should add Entra ID user auth.
