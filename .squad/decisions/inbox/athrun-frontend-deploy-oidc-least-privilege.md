# 2026-06-07T06:38:03.974-04:00 — Frontend deploy uses OIDC with Web App-scoped RBAC

- **By:** Athrun / Coordinator
- **Decision:** GitHub Actions frontend deployment uses Azure workload identity federation for `sp-call-center` and avoids storing a client secret. The service principal is scoped to the frontend App Service with `Website Contributor` instead of resource-group-wide `Contributor`.
- **Why:** This satisfies the frontend-only deployment requirement while preserving least privilege and reducing credential leakage risk.
- **Source evidence:** `.github/workflows/deploy-frontend.yml`, GitHub repository secrets/variables, Azure federated credential `github-production-frontend`.
