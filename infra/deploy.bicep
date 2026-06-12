targetScope = 'subscription'

@description('Name of the resource group to create or update for the solution deployment.')
param resourceGroupName string

@description('Azure region for the resource group and region-bound resources.')
param location string

@description('Approved global exception for Azure Translator.')
param translatorLocation string = 'global'

@description('Approved global exception for Azure Communication Services.')
param communicationLocation string = 'global'

@description('Data location for Azure Communication Services. Must be United States to acquire US toll-free numbers.')
param communicationDataLocation string = 'United States'

@description('Placeholder image used until the API service image is available in Azure Container Registry.')
param apiContainerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Enable ACA liveness/readiness probes only after the real API image is deployed and /healthz is confirmed.')
param enableApiHealthProbes bool = false

@description('Create the ACS IncomingCall Event Grid webhook subscription. Enable only after the real API image is deployed and the webhook validation endpoint is live.')
param enableAcsIncomingCallSubscription bool = false

@description('Comma-separated candidate languages for Speech auto-detection.')
param speechCandidateLanguages string = 'en-US,sv-SE,de-DE,fr-FR'

@description('Placeholder Azure AI deployment name until a model is deployed post-provision.')
param foundryDeploymentName string = 'post-provision-model-deployment'

@description('Minimum replicas for the API Container App. Set to 1 for demo reliability.')
param apiMinReplicas int = 1

@description('Optional additional tags applied to the resource group and all resources.')
param tags object = {}

var workloadName = 'callcentertranscription'
var mergedTags = union(
  {
    workload: workloadName
    managedBy: 'bicep'
    environment: 'poc'
  },
  tags
)

resource targetResourceGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
  tags: mergedTags
}

module solution 'main.bicep' = {
  name: 'deploy-${workloadName}-${uniqueString(resourceGroupName, location)}'
  scope: targetResourceGroup
  params: {
    location: location
    translatorLocation: translatorLocation
    communicationLocation: communicationLocation
    communicationDataLocation: communicationDataLocation
    apiContainerImage: apiContainerImage
    enableApiHealthProbes: enableApiHealthProbes
    enableAcsIncomingCallSubscription: enableAcsIncomingCallSubscription
    speechCandidateLanguages: speechCandidateLanguages
    foundryDeploymentName: foundryDeploymentName
    apiMinReplicas: apiMinReplicas
    tags: tags
  }
}

output resourceGroupName string = targetResourceGroup.name
output resourceGroupId string = targetResourceGroup.id
output resourceNames object = solution.outputs.resourceNames
output serviceEndpoints object = solution.outputs.serviceEndpoints
output managedIdentityPrincipalIds object = solution.outputs.managedIdentityPrincipalIds
output managedIdentityResourceIds object = solution.outputs.managedIdentityResourceIds
output identityDesign object = solution.outputs.identityDesign
output manualPostProvisionSteps array = solution.outputs.manualPostProvisionSteps
