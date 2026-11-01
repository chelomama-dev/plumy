// Key Vault for the secrets Entra ID can't replace: today, Stripe's API key and webhook signing secret (ADR 0012).
// Access by Azure RBAC only (no access policies). Bicep creates the vault and who may read it; the secrets
// themselves are set by a person with az keyvault secret set, so their values never pass through the repo,
// the pipeline or a deployment's history (infra/README.md).
param location string
@description('Globally unique, 3-24 letters, digits and hyphens.')
@minLength(3)
@maxLength(24)
param vaultName string
param tags object
@description('Principal (the API\'s managed identity) that reads the secrets.')
param readerPrincipalId string

resource vault 'Microsoft.KeyVault/vaults@2026-05-15' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      // Standard: secrets don't need HSM-backed keys (Premium)
      name: 'standard'
    }
    // Roles decide who reads what, as for Storage and Service Bus; access policies are the old model
    enableRbacAuthorization: true
    // A deleted secret, or the whole vault, can be recovered for a week
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    // Purge protection can never be turned off once on, and it would stop the vault's name being reused for the
    // retention days after a delete. Worth it with production data in the vault; for two sandbox keys, not yet
    enablePurgeProtection: null
    // Public endpoint, Entra ID required: the Consumption environment has no virtual network to put a private
    // endpoint in. Nothing gets in without a role on the vault
    publicNetworkAccess: 'Enabled'
  }
}

// Built-in role "Key Vault Secrets User": read secret values, nothing else (no listing keys, no writing)
var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// On the vault, since the secrets aren't Bicep resources. The vault holds only the API's secrets
resource apiSecretsAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, readerPrincipalId, secretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = vault.name
// e.g. https://kv-plumy-xxx.vault.azure.net/
output vaultUri string = vault.properties.vaultUri
