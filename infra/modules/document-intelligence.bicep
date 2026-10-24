// Azure Document Intelligence, which reads the uploaded invoices (prebuilt invoice model). Entra ID only, like
// Storage and Service Bus: no keys.
param location string
@description('Globally unique: it is also the custom subdomain, <name>.cognitiveservices.azure.com.')
@minLength(2)
@maxLength(64)
param accountName string
param tags object
@description('F0: free, 500 pages a month, only the first 2 pages of each file and files up to 4 MB; one F0 account per subscription. S0: paid per page, no such limits.')
@allowed(['F0', 'S0'])
param sku string
@description('Principal that analyses documents (the worker\'s managed identity).')
param userPrincipalId string

resource account 'Microsoft.CognitiveServices/accounts@2026-09-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'FormRecognizer'
  sku: {
    name: sku
  }
  properties: {
    // Entra ID tokens only work against the account's own subdomain, not the shared regional endpoint
    customSubDomainName: accountName
    // No API keys: only identities with a role on the account get in
    disableLocalAuth: true
    // Container Apps on Consumption have no fixed outbound IPs to allow, and closing it takes a VNet and a private
    // endpoint (paid). Entra ID is what protects it
    publicNetworkAccess: 'Enabled'
  }
}

// Cognitive Services User: call the account's APIs (analyse documents), nothing about managing it
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
output endpoint string = account.properties.endpoint
