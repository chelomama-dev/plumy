// Azure SQL logical server + serverless database on the free offer, Entra ID authentication only
param location string
param serverName string
param databaseName string
param tags object
param adminLogin string
param adminObjectId string
param adminPrincipalType string
param clientIpAddress string

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // No SQL logins at all: no password exists that could leak. Apps use managed identities, people use Entra ID.
    administrators: {
      administratorType: 'ActiveDirectory'
      login: adminLogin
      sid: adminObjectId
      tenantId: tenant().tenantId
      principalType: adminPrincipalType
      azureADOnlyAuthentication: true
    }
  }
}

// Container Apps on the Consumption plan has no fixed outbound IPs, so the firewall has to let in Azure services.
// That opens the network path to any Azure tenant, but without a valid Entra ID token nobody gets in. Closing it
// completely needs a VNet + private endpoint, which is not free.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowClient 'Microsoft.Sql/servers/firewallRules@2025-01-01' = if (!empty(clientIpAddress)) {
  parent: server
  name: 'AllowDeveloperIp'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  // Serverless General Purpose: the only SKU the free offer applies to
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    // Free offer: 100,000 vCore seconds + 32 GB per month. When they run out, pause until next month
    // instead of billing the overage.
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    // Pause after 1 h without connections (the minimum), so idle time does not eat the free vCore seconds
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB, the free offer limit
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
