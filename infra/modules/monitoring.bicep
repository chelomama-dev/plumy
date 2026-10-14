// Log Analytics workspace: where Container Apps sends console logs and system events
param location string
param name string
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    // Hard cap on ingestion (0.1 GB/day): a log loop cannot turn into a bill
    workspaceCapping: {
      dailyQuotaGb: json('0.1')
    }
  }
}

output name string = workspace.name
