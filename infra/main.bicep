// Plumy infrastructure: Container Apps (web + API) and Azure SQL (free offer) in one resource group.
// Deploy with: az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam (see infra/README.md)
targetScope = 'resourceGroup'

@description('Azure region for every resource. Defaults to the resource group region.')
param location string = resourceGroup().location

@description('Short name used to build resource names.')
param appName string = 'plumy'

@description('Full API image reference, e.g. ghcr.io/owner/plumy-api:sha-abc1234. Use an immutable sha tag, not latest.')
param apiImage string

@description('Full web image reference, e.g. ghcr.io/owner/plumy-web:sha-abc1234.')
param webImage string

@description('Custom domain for the web, e.g. app.getplumy.com. Its DNS records must exist before deploying (infra/README.md).')
param webCustomDomain string = ''

@description('Entra ID principal that administers the SQL server: display name or UPN.')
param sqlAdminLogin string

@description('Object ID of that Entra ID principal.')
param sqlAdminObjectId string

@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string = 'Group'

@description('Public IP allowed through the SQL firewall, to run migrations from your machine. Empty = no rule.')
param clientIpAddress string = ''

var tags = {
  app: appName
  managedBy: 'bicep'
}

// User-assigned (not system-assigned) so it exists before the API: the SQL user can be created for it
// independently, and it survives the container app being deleted and recreated.
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-${appName}-api'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  params: {
    location: location
    name: 'log-${appName}'
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  params: {
    location: location
    // SQL server names are global DNS names, so they need a unique suffix
    serverName: 'sql-${appName}-${uniqueString(resourceGroup().id)}'
    databaseName: '${appName}db'
    tags: tags
    adminLogin: sqlAdminLogin
    adminObjectId: sqlAdminObjectId
    adminPrincipalType: sqlAdminPrincipalType
    clientIpAddress: clientIpAddress
  }
}

module apps 'modules/container-apps.bicep' = {
  params: {
    location: location
    appName: appName
    tags: tags
    workspaceName: monitoring.outputs.name
    apiImage: apiImage
    webImage: webImage
    webCustomDomain: webCustomDomain
    apiIdentityId: apiIdentity.id
    apiIdentityClientId: apiIdentity.properties.clientId
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
  }
}

output webUrl string = apps.outputs.webUrl
// What the custom domain's DNS records point to (CNAME) and prove (TXT asuid)
output webDefaultFqdn string = apps.outputs.webDefaultFqdn
output domainVerificationId string = apps.outputs.domainVerificationId
output apiInternalFqdn string = apps.outputs.apiFqdn
output sqlServerFqdn string = sql.outputs.serverFqdn
output databaseName string = sql.outputs.databaseName
output apiIdentityName string = apiIdentity.name
output apiIdentityClientId string = apiIdentity.properties.clientId
