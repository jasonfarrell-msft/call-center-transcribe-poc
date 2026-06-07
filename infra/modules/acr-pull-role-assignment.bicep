targetScope = 'resourceGroup'

@description('Target scope type for the role assignment.')
@allowed([
  'acr'
  'keyVault'
  'cognitiveServices'
])
param scopeType string

@description('Name of the target scope resource receiving the role assignment.')
param scopeName string

@description('Principal ID of the managed identity that needs the role assignment.')
param principalId string

@description('Role definition resource ID to assign at the target scope.')
param roleDefinitionId string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = if (scopeType == 'acr') {
  name: scopeName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = if (scopeType == 'keyVault') {
  name: scopeName
}

resource cognitiveAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = if (scopeType == 'cognitiveServices') {
  name: scopeName
}

resource acrScopedRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (scopeType == 'acr') {
  name: guid(registry.id, principalId, roleDefinitionId)
  scope: registry
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: roleDefinitionId
  }
}

resource keyVaultScopedRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (scopeType == 'keyVault') {
  name: guid(keyVault.id, principalId, roleDefinitionId)
  scope: keyVault
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: roleDefinitionId
  }
}

resource cognitiveScopedRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (scopeType == 'cognitiveServices') {
  name: guid(cognitiveAccount.id, principalId, roleDefinitionId)
  scope: cognitiveAccount
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: roleDefinitionId
  }
}
