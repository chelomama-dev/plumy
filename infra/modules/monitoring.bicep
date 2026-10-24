// Log Analytics workspace (console logs and system events from Container Apps) and Application Insights on top of
// it (traces, metrics and logs the apps send with OpenTelemetry). Both write to the same workspace, under its cap.
param location string
param name string
param appInsightsName string
param tags object
@description('Principal IDs allowed to send telemetry to Application Insights (the apps\' managed identities).')
param publisherPrincipalIds array

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    // Hard cap on ingestion (0.1 GB/day): a log loop cannot turn into a bill. Application Insights data counts too
    workspaceCapping: {
      dailyQuotaGb: json('0.1')
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    // Workspace-based: the data lives in the workspace above (AppRequests, AppDependencies, AppTraces...)
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    // Only Entra ID tokens are accepted: the instrumentation key in the connection string is not enough to send
    DisableLocalAuth: true
  }
}

// Despite the name, this role is what lets an identity send any telemetry to Application Insights
var monitoringMetricsPublisher = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '3913510d-42f4-4e42-8a64-420c390055eb'
)

resource publishers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in publisherPrincipalIds: {
    name: guid(appInsights.id, principalId, monitoringMetricsPublisher)
    scope: appInsights
    properties: {
      roleDefinitionId: monitoringMetricsPublisher
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

output name string = workspace.name
// Not a secret with local auth disabled: it only says where to send, the token says who is sending
output appInsightsConnectionString string = appInsights.properties.ConnectionString
