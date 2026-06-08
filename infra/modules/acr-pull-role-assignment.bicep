targetScope = 'resourceGroup'

@description('Target scope type for the role assignment.')
@allowed([
  'acr'
  'keyVault'
  'cognitiveServices'
  'communicationServices'
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

resource communicationServicesAccount 'Microsoft.Communication/communicationServices@2025-05-01' existing = if (scopeType == 'communicationServices') {
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

// Communication Services role assignment.
// No narrower built-in role covers Call Automation AnswerCall + StartMediaStreaming than
// Communication Services Contributor. Mitigated: scoped to the single ACS resource only
// (not RG/sub), assigned to a system-assigned MI with no external exposure. Narrow when
// Microsoft ships a dedicated Call Automation built-in role.
resource communicationServicesScopedRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (scopeType == 'communicationServices') {
  name: guid(communicationServicesAccount.id, principalId, roleDefinitionId)
  scope: communicationServicesAccount
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: roleDefinitionId
  }
}
