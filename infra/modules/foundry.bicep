// Microsoft Foundry: a language model the worker asks for the ledger account of invoices from new suppliers
// (ADR 0005). Entra ID only, like Document Intelligence: no keys.
param location string
@description('Globally unique: it is also the custom subdomain, <name>.openai.azure.com.')
@minLength(2)
@maxLength(64)
param accountName string
param tags object
@description('Model to deploy, e.g. gpt-5.4-mini. The deployment takes the same name.')
param modelName string
@description('Model version, e.g. 2026-03-17. Pinned: a new version can answer differently.')
param modelVersion string
@description('Thousands of tokens per minute. Each suggestion takes about 2K; this is a ceiling, billing is per token.')
@minValue(1)
param capacity int
@description('Principal that calls the model (the worker\'s managed identity).')
param userPrincipalId string

// AIServices: the Foundry resource kind that serves OpenAI models (and others) through one endpoint
resource account 'Microsoft.CognitiveServices/accounts@2026-09-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    // Entra ID tokens only work against the account's own subdomain
    customSubDomainName: accountName
    // No API keys: only identities with a role on the account get in
    disableLocalAuth: true
    // Same reasoning as Document Intelligence: no fixed outbound IPs on Consumption, Entra ID protects it
    publicNetworkAccess: 'Enabled'
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2026-09-01' = {
  parent: account
  name: modelName
  sku: {
    // Data Zone Standard: requests are processed inside the EU data zone (invoices are customers' data), pay per
    // token. Global Standard would route them anywhere in the world
    name: 'DataZoneStandard'
    capacity: capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
    // Keeps the pinned version until it retires, then moves to the default one instead of stopping
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
}

// Cognitive Services User: call the account's models, nothing about managing it. The role Microsoft documents for
// keyless inference, and the one the pipeline is already allowed to assign (bootstrap-github-oidc.sh)
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource userAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, userPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: userPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = account.name
// The OpenAI-compatible endpoint of the custom subdomain; the app adds /openai/v1/
output endpoint string = 'https://${account.properties.customSubDomainName}.openai.azure.com/'
// Depends on the deployment, so whoever uses it waits for the model to exist
output deploymentName string = deployment.name
