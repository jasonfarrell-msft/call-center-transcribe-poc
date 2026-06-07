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
