// Plumy infrastructure: Container Apps (web, API, worker), Azure SQL (free offer), Blob Storage, Service Bus,
// Document Intelligence, Microsoft Foundry, Application Insights and alerts in one resource group.
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

@description('Full worker image reference, e.g. ghcr.io/owner/plumy-worker:sha-abc1234.')
param workerImage string

@description('Whether the API sends uploaded invoices to the worker to be read. Off: they are stored and stay Pending.')
param invoiceExtractionEnabled bool = true

@description('Document Intelligence tier. F0 is free but reads only 2 pages of each file, up to 4 MB, 500 pages a month.')
@allowed(['F0', 'S0'])
param documentIntelligenceSku string = 'F0'

@description('Thousands of tokens per minute for the ledger account model. A ceiling, not a reservation: billing is per token.')
@minValue(1)
param ledgerAccountModelCapacity int = 10

@description('Custom domain for the web, e.g. app.getplumy.com. Its DNS records must exist before deploying (infra/README.md).')
param webCustomDomain string = ''

@description('Entra ID principal that administers the SQL server: display name or UPN.')
param sqlAdminLogin string

@description('Object ID of that Entra ID principal.')
param sqlAdminObjectId string

@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string = 'Group'

@description('Email address that receives the alerts.')
param alertEmail string

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

// The worker's own identity: it only needs to read invoices and their files, and receive from the queue
resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-${appName}-worker'
  location: location
  tags: tags
}

// Must match the worker's InvoiceExtraction:MaxDeliveryCount (and the AppHost)
var invoiceExtractionMaxDeliveryCount = 5

module monitoring 'modules/monitoring.bicep' = {
  params: {
    location: location
    name: 'log-${appName}'
    appInsightsName: 'appi-${appName}'
    tags: tags
    publisherPrincipalIds: [
      apiIdentity.properties.principalId
      workerIdentity.properties.principalId
    ]
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

module storage 'modules/storage.bicep' = {
  params: {
    location: location
    // Storage account names are global DNS names too: lowercase letters and digits only, 24 at most
    accountName: 'st${appName}${uniqueString(resourceGroup().id)}'
    tags: tags
    containerName: 'invoices'
    dataContributorPrincipalId: apiIdentity.properties.principalId
    dataReaderPrincipalId: workerIdentity.properties.principalId
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  params: {
    location: location
    // Global DNS name too
    namespaceName: 'sb-${appName}-${uniqueString(resourceGroup().id)}'
    tags: tags
    queueName: 'invoice-extraction'
    maxDeliveryCount: invoiceExtractionMaxDeliveryCount
    senderPrincipalId: apiIdentity.properties.principalId
    receiverPrincipalId: workerIdentity.properties.principalId
  }
}

module documentIntelligence 'modules/document-intelligence.bicep' = {
  params: {
    location: location
    // Global DNS name too (the custom subdomain)
    accountName: 'di-${appName}-${uniqueString(resourceGroup().id)}'
    tags: tags
    sku: documentIntelligenceSku
    userPrincipalId: workerIdentity.properties.principalId
  }
}

// gpt-5.4-mini: GA in France Central as Data Zone Standard, inference until 2027-09-21 (checked 2026-10-24, ADR 0005)
module foundry 'modules/foundry.bicep' = {
  params: {
    location: location
    // Global DNS name too (the custom subdomain)
    accountName: 'aif-${appName}-${uniqueString(resourceGroup().id)}'
    tags: tags
    modelName: 'gpt-5.4-mini'
    modelVersion: '2026-03-17'
    capacity: ledgerAccountModelCapacity
    userPrincipalId: workerIdentity.properties.principalId
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
    workerImage: workerImage
    webCustomDomain: webCustomDomain
    apiIdentityId: apiIdentity.id
    apiIdentityClientId: apiIdentity.properties.clientId
    workerIdentityId: workerIdentity.id
    workerIdentityClientId: workerIdentity.properties.clientId
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
    // A module output: the apps wait for the whole storage module, role assignment included
    blobEndpoint: storage.outputs.blobEndpoint
    // Module outputs as well: the apps wait for the queue and its role assignments
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    serviceBusHostName: serviceBus.outputs.hostName
    invoiceExtractionQueueName: 'invoice-extraction'
    invoiceExtractionEnabled: invoiceExtractionEnabled
    invoiceExtractionMaxDeliveryCount: invoiceExtractionMaxDeliveryCount
    // A module output: the worker waits for the account and its role assignment
    documentIntelligenceEndpoint: documentIntelligence.outputs.endpoint
    // Module outputs: the worker waits for the account, the model and its role assignment
    foundryEndpoint: foundry.outputs.endpoint
    ledgerAccountModelDeployment: foundry.outputs.deploymentName
    // A module output: the apps wait for Application Insights and their role on it
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
  }
}

module alerts 'modules/alerts.bicep' = {
  params: {
    appName: appName
    tags: tags
    alertEmail: alertEmail
    apiAppId: apps.outputs.apiId
    serviceBusNamespaceId: serviceBus.outputs.namespaceId
    invoiceExtractionQueueName: 'invoice-extraction'
  }
}

output webUrl string = apps.outputs.webUrl
// What the custom domain's DNS records point to (CNAME) and prove (TXT asuid)
output webDefaultFqdn string = apps.outputs.webDefaultFqdn
output domainVerificationId string = apps.outputs.domainVerificationId
output apiInternalFqdn string = apps.outputs.apiFqdn
output sqlServerFqdn string = sql.outputs.serverFqdn
output databaseName string = sql.outputs.databaseName
output storageAccountName string = storage.outputs.accountName
output documentIntelligenceName string = documentIntelligence.outputs.name
output foundryName string = foundry.outputs.name
output apiIdentityName string = apiIdentity.name
output apiIdentityClientId string = apiIdentity.properties.clientId
output workerIdentityName string = workerIdentity.name
output workerIdentityClientId string = workerIdentity.properties.clientId
