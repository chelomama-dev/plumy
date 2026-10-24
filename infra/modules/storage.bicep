// Blob Storage for the uploaded invoices. Private, Entra ID only: no account keys, no SAS, no public blobs.
param location string
@description('Globally unique, 3-24 lowercase letters and digits.')
@minLength(3)
@maxLength(24)
param accountName string
param tags object
param containerName string
@description('Principal (the API\'s managed identity) that reads and writes the blobs.')
param dataContributorPrincipalId string
@description('Principal (the worker\'s managed identity) that only reads them.')
param dataReaderPrincipalId string

resource account 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'StorageV2'
  // Locally redundant: three copies in one datacenter. Enough for a side project; ZRS/GRS cost more
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // Keys and SAS tokens are passwords in disguise: with this off, only Entra ID identities get in
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: account
  name: 'default'
  properties: {
    // Soft delete: a deleted invoice (or container) can be recovered for a week
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// Built-in role "Storage Blob Data Contributor": read, write and delete blobs; no account management, no keys
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// On the account, not just the container: the API's health check lists the account's containers.
// The account holds nothing else, so the reach is the same
resource apiBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  // Deterministic name: redeploying finds the same assignment instead of failing on a duplicate
  name: guid(account.id, dataContributorPrincipalId, blobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: dataContributorPrincipalId
    // Tells Azure it's a service principal, so it doesn't wait for a just-created identity to replicate in Entra ID
    principalType: 'ServicePrincipal'
  }
}

// Built-in role "Storage Blob Data Reader". On the container only: the worker reads invoices and nothing else
var blobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

resource workerBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, dataReaderPrincipalId, blobDataReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataReaderRoleId)
    principalId: dataReaderPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output blobEndpoint string = account.properties.primaryEndpoints.blob
output accountName string = account.name
