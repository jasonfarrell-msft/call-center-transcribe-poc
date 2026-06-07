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
