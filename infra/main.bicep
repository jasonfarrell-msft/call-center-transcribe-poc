targetScope = 'resourceGroup'

@description('Primary location for region-bound resources.')
param location string = 'swedencentral'

@description('Approved global exception for Azure Translator.')
param translatorLocation string = 'global'

@description('Approved global exception for Azure Communication Services.')
param communicationLocation string = 'global'

// IMMUTABLE — dataLocation cannot be changed in-place after resource creation.
// Switching from 'Europe' to 'United States' requires the ACS resource to be deleted and
// recreated on next provision. This is intentional: enables US toll-free number acquisition;
// there are no sunk assets (no number purchased, no Event Grid subscription wired).
// The Communication Services Contributor role assignment re-applies automatically via its
// deterministic guid() name scoped to the new resource id.
@description('Data location for Azure Communication Services. Must be United States to acquire US toll-free numbers.')
param communicationDataLocation string = 'United States'

@description('Placeholder image used until the API service image is available in Azure Container Registry.')
param apiContainerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Enable ACA liveness/readiness probes only after the real API image is deployed and /healthz is confirmed.')
param enableApiHealthProbes bool = false

@description('Comma-separated candidate languages for Speech auto-detection.')
param speechCandidateLanguages string = 'en-US,sv-SE,de-DE,fr-FR'

@description('Placeholder Azure AI deployment name until a model is deployed post-provision.')
param foundryDeploymentName string = 'post-provision-model-deployment'

@description('Minimum replicas for the API Container App. Set to 1 for demo reliability (WebSocket statefulness — a cold replica would drop an inbound call).')
param apiMinReplicas int = 1

@description('Optional additional tags applied to all resources.')
param tags object = {}

var workloadName = 'callcentertranscription'
var shortWorkloadName = 'cctrans'
var uniqueSuffix = substring(uniqueString(subscription().subscriptionId, resourceGroup().id), 0, 6)
var mergedTags = union(
  {
    workload: workloadName
    managedBy: 'bicep'
    environment: 'poc'
  },
  tags
)

var logAnalyticsWorkspaceName = 'law-${shortWorkloadName}-${uniqueSuffix}'
var applicationInsightsName = 'appi-${shortWorkloadName}-${uniqueSuffix}'
var keyVaultName = 'kv-${shortWorkloadName}-${uniqueSuffix}'
var containerRegistryName = 'acr${shortWorkloadName}${uniqueSuffix}'
var containerAppsEnvironmentName = 'cae-${shortWorkloadName}-${uniqueSuffix}'
var apiContainerAppName = 'ca-api-${shortWorkloadName}-${uniqueSuffix}'
var acrPullUserAssignedIdentityName = 'uami-acrpull-${shortWorkloadName}-${uniqueSuffix}'
var appServicePlanName = 'asp-${shortWorkloadName}-${uniqueSuffix}'
var webAppName = 'web-${shortWorkloadName}-${uniqueSuffix}'
var communicationServiceName = 'acs-${shortWorkloadName}-${uniqueSuffix}'
var speechAccountName = 'speech-${shortWorkloadName}-${uniqueSuffix}'
var speechCustomSubdomainName = 'speech${shortWorkloadName}${uniqueSuffix}'
var translatorAccountName = 'translator-${shortWorkloadName}-${uniqueSuffix}'
var translatorCustomSubdomainName = 'translator${shortWorkloadName}${uniqueSuffix}'
var aiServicesAccountName = 'ai-${shortWorkloadName}-${uniqueSuffix}'
var aiServicesCustomSubdomainName = 'ai${shortWorkloadName}${uniqueSuffix}'

var logAnalyticsApiVersion = '2023-09-01'

var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)
var cognitiveServicesUserRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'a97b65f3-24c7-4388-baec-2e87135dc908'
)
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)
// Communication Services Contributor — minimum viable built-in role for Call Automation
// AnswerCall + StartMediaStreaming. No narrower role covers both operations. Accepted for
// POC because the assignment is resource-scoped to ACS only and the identity is a
// system-assigned MI with no external exposure.
var communicationServicesContributorRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '2b4609a5-7812-4aba-b5e3-076e6a078419'
)

var speechEndpoint = 'https://${speechCustomSubdomainName}.cognitiveservices.azure.com/'
var translatorEndpoint = 'https://${translatorCustomSubdomainName}.cognitiveservices.azure.com/'
var aiServicesEndpoint = 'https://${aiServicesCustomSubdomainName}.cognitiveservices.azure.com/'
var acsEndpoint = 'https://${communicationServiceName}.communication.azure.com/'
var apiFqdn = '${apiContainerAppName}.${containerAppsEnvironment.properties.defaultDomain}'
var apiBaseUrl = 'https://${apiFqdn}'
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  tags: mergedTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  tags: mergedTags
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    IngestionMode: 'LogAnalytics'
    Request_Source: 'rest'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: mergedTags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    softDeleteRetentionInDays: 90
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
    }
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  tags: mergedTags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    dataEndpointEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: mergedTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: listKeys(logAnalyticsWorkspace.id, logAnalyticsApiVersion).primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

resource speechAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: speechAccountName
  location: location
  kind: 'SpeechServices'
  tags: mergedTags
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: speechCustomSubdomainName
    publicNetworkAccess: 'Enabled'
  }
}

resource translatorAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: translatorAccountName
  location: translatorLocation
  kind: 'TextTranslation'
  tags: mergedTags
  sku: {
    name: 'S1'
  }
  properties: {
    customSubDomainName: translatorCustomSubdomainName
    publicNetworkAccess: 'Enabled'
  }
}

resource aiServicesAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: aiServicesAccountName
  location: location
  kind: 'AIServices'
  tags: mergedTags
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: aiServicesCustomSubdomainName
    publicNetworkAccess: 'Enabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2025-05-01' = {
  name: communicationServiceName
  location: communicationLocation
  tags: mergedTags
  properties: {
    dataLocation: communicationDataLocation
  }
}

resource acrPullUserAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: acrPullUserAssignedIdentityName
  location: location
  tags: mergedTags
}

resource apiContainerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiContainerAppName
  location: location
  tags: union(mergedTags, {
    'azd-service-name': 'api'
  })
  identity: {
    type: 'SystemAssigned,UserAssigned'
    userAssignedIdentities: {
      '${acrPullUserAssignedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: acrPullUserAssignedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiContainerImage
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationInsights.properties.ConnectionString
            }
            {
              name: 'Security__RequireAuth'
              value: 'false'
            }
            {
              name: 'Speech__Endpoint'
              value: speechEndpoint
            }
            {
              name: 'Speech__CandidateLanguages'
              value: speechCandidateLanguages
            }
            {
              name: 'Translator__Endpoint'
              value: translatorEndpoint
            }
            {
              name: 'Foundry__Endpoint'
              value: aiServicesEndpoint
            }
            {
              name: 'Foundry__DeploymentName'
              value: foundryDeploymentName
            }
            {
              name: 'Acs__Endpoint'
              value: acsEndpoint
            }
            {
              name: 'AudioSource__Mode'
              // 'Mock' keeps DI wired to MockAudioSource (default). Set to 'Acs' after the
              // ACS phone number and Event Grid subscription are provisioned to activate the
              // live AcsAudioSource — no image rebuild required (Dyakka reads AudioSource:Mode
              // via IConfiguration; double-underscore maps to the colon-separated key).
              value: 'Mock'
            }
          ]
          probes: enableApiHealthProbes ? [
            {
              type: 'Liveness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 15
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ] : []
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: 1
      }
    }
  }
}

module apiContainerAppAcrPull 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiContainerAppAcrPull'
  params: {
    scopeType: 'acr'
    scopeName: containerRegistry.name
    principalId: acrPullUserAssignedIdentity.properties.principalId
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  tags: mergedTags
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  tags: union(mergedTags, {
    'azd-service-name': 'web'
  })
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      http20Enabled: true
      linuxFxVersion: 'DOTNETCORE|9.0'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'BackendApi__BaseUrl'
          value: apiBaseUrl
        }
      ]
    }
  }
}

module apiToKeyVaultRoleAssignment 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiToKeyVaultRoleAssignment'
  params: {
    scopeType: 'keyVault'
    scopeName: keyVault.name
    principalId: apiContainerApp.identity.principalId
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
  }
}

module apiToSpeechRoleAssignment 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiToSpeechRoleAssignment'
  params: {
    scopeType: 'cognitiveServices'
    scopeName: speechAccount.name
    principalId: apiContainerApp.identity.principalId
    roleDefinitionId: cognitiveServicesUserRoleDefinitionId
  }
}

module apiToTranslatorRoleAssignment 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiToTranslatorRoleAssignment'
  params: {
    scopeType: 'cognitiveServices'
    scopeName: translatorAccount.name
    principalId: apiContainerApp.identity.principalId
    roleDefinitionId: cognitiveServicesUserRoleDefinitionId
  }
}

module apiToAiServicesRoleAssignment 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiToAiServicesRoleAssignment'
  params: {
    scopeType: 'cognitiveServices'
    scopeName: aiServicesAccount.name
    principalId: apiContainerApp.identity.principalId
    roleDefinitionId: cognitiveServicesUserRoleDefinitionId
  }
}

// ACS data-plane RBAC: Communication Services Contributor scoped to the ACS resource only.
// Allows Call Automation AnswerCall + StartMediaStreaming via DefaultAzureCredential (system MI).
// Scope is the single ACS resource, not the resource group — least-privilege for a POC.
module apiToAcsRoleAssignment 'modules/acr-pull-role-assignment.bicep' = {
  name: 'apiToAcsRoleAssignment'
  params: {
    scopeType: 'communicationServices'
    scopeName: communicationService.name
    principalId: apiContainerApp.identity.principalId
    roleDefinitionId: communicationServicesContributorRoleDefinitionId
  }
}

// TODO(event-grid): Deliberately deferred. The codebase does not yet expose a verified
// ACS incoming-call webhook and media-streaming route surface, so this template avoids
// creating an invalid or unusable Event Grid system topic/subscription until those routes
// are implemented and validated.

// TODO(ai-foundry-project): Deliberately deferred. A regional Azure AI Services account is
// provisioned now, but project/model deployment should be completed as a post-provision step
// once the exact model/deployment contract is finalized.

output resourceNames object = {
  logAnalyticsWorkspace: logAnalyticsWorkspace.name
  applicationInsights: applicationInsights.name
  keyVault: keyVault.name
  containerRegistry: containerRegistry.name
  acrPullUserAssignedIdentity: acrPullUserAssignedIdentity.name
  containerAppsEnvironment: containerAppsEnvironment.name
  apiContainerApp: apiContainerApp.name
  appServicePlan: appServicePlan.name
  webApp: webApp.name
  communicationService: communicationService.name
  speechAccount: speechAccount.name
  translatorAccount: translatorAccount.name
  aiServicesAccount: aiServicesAccount.name
}

output serviceEndpoints object = {
  apiBaseUrl: apiBaseUrl
  apiHealthUrl: '${apiBaseUrl}/healthz'
  acsLiveAutomationStatus: 'Deferred until API ACS callback/media routes are implemented and validated.'
  webBaseUrl: 'https://${webApp.properties.defaultHostName}'
  webHealthUrl: 'https://${webApp.properties.defaultHostName}/healthz'
  keyVaultUri: keyVault.properties.vaultUri
  containerRegistryLoginServer: containerRegistry.properties.loginServer
  speechEndpoint: speechEndpoint
  translatorEndpoint: translatorEndpoint
  foundryEndpoint: aiServicesEndpoint
  acsEndpoint: acsEndpoint
}

output managedIdentityPrincipalIds object = {
  apiRuntimeSystemAssigned: apiContainerApp.identity.principalId
  apiAcrPullUserAssigned: acrPullUserAssignedIdentity.properties.principalId
  api: apiContainerApp.identity.principalId
  web: webApp.identity.principalId
}

output managedIdentityResourceIds object = {
  apiAcrPullUserAssigned: acrPullUserAssignedIdentity.id
}

output identityDesign object = {
  apiRuntimeIdentity: 'SystemAssigned (for Key Vault/Cognitive/ACS runtime access)'
  apiRuntimePrincipalId: apiContainerApp.identity.principalId
  apiAcrPullIdentity: 'UserAssigned (for ACR pulls only)'
  apiAcrPullUserAssignedIdentityResourceId: acrPullUserAssignedIdentity.id
  apiAcrPullUserAssignedIdentityPrincipalId: acrPullUserAssignedIdentity.properties.principalId
  registriesBehavior: 'ACA registries always binds this deployment ACR to the ACR-pull UAMI so azd deploy can update the placeholder image to an ACR-hosted revision.'
}

output manualPostProvisionSteps array = [
  'Deploy the API image to ${containerRegistry.properties.loginServer}/api:<tag> and update the Container App image.'
  'ACR pulls are split to user-assigned identity ${acrPullUserAssignedIdentity.name}; runtime Key Vault/Cognitive/ACS access remains on the API Container App system-assigned identity principal ${apiContainerApp.identity.principalId}.'
  'The Container App registry binding is preconfigured for ${containerRegistry.properties.loginServer} with UAMI ${acrPullUserAssignedIdentity.id} so azd deploy can pull ACR-hosted images.'
  'After deploying the real API image, set enableApiHealthProbes=true and re-apply infrastructure once ${apiBaseUrl}/healthz returns healthy responses from the API runtime.'
  'Implement and validate the ACS incoming-call webhook at ${apiBaseUrl}/api/events/acs/incoming-call before adding Event Grid.'
  'Implement and validate the ACS media WebSocket endpoint at wss://${apiFqdn}/api/calls/media-stream before enabling live ACS automation.'
  'ACS data-plane RBAC (Communication Services Contributor) is already assigned to the API Container App system-assigned identity, scoped to the ACS resource. No manual role assignment needed.'
  'To activate live ACS audio, set AudioSource__Mode=Acs on the Container App env vars after provisioning the ACS phone number and Event Grid subscription.'
  'Create the Azure AI project/model deployment against ${aiServicesAccount.name} and then set Foundry__DeploymentName to the final deployed model name.'
  'If App Service Linux does not yet accept DOTNETCORE|9.0 in the target stamp, switch linuxFxVersion to the latest supported .NET runtime during first deployment.'
]
