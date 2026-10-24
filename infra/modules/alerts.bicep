// Alerts that email you when something goes wrong, so you don't have to look just in case.
// Metric alerts: Azure evaluates them on platform metrics, so they work even if the app sends no telemetry.
param appName string
param tags object
@description('Where the alerts are sent.')
param alertEmail string
param apiAppId string
param serviceBusNamespaceId string
param invoiceExtractionQueueName string

resource emailMe 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${appName}-email'
  // Action groups and metric alerts are global resources
  location: 'global'
  tags: tags
  properties: {
    groupShortName: appName
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// Any 5xx from the API: the users got an error. With this little traffic, even one is worth a look
resource apiErrors 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${appName}-api-5xx'
  location: 'global'
  tags: tags
  properties: {
    description: 'The API answered with a server error (5xx). Look for the exception in Application Insights (Failures).'
    severity: 2
    enabled: true
    scopes: [apiAppId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    // Resolves itself when a window has no 5xx
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'server-errors'
          metricNamespace: 'Microsoft.App/containerApps'
          metricName: 'Requests'
          dimensions: [
            {
              name: 'statusCodeCategory'
              operator: 'Include'
              values: ['5xx']
            }
          ]
          timeAggregation: 'Total'
          operator: 'GreaterThan'
          threshold: 0
        }
      ]
    }
    actions: [
      {
        actionGroupId: emailMe.id
      }
    ]
  }
}

// A message in the dead-letter queue is an invoice nobody will process: a malformed message or a deleted invoice
resource deadLetters 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${appName}-dead-letters'
  location: 'global'
  tags: tags
  properties: {
    description: 'Messages in the ${invoiceExtractionQueueName} dead-letter queue. Read them with Service Bus Explorer (portal) and see infra/README.md.'
    severity: 2
    enabled: true
    scopes: [serviceBusNamespaceId]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'dead-lettered'
          metricNamespace: 'Microsoft.ServiceBus/namespaces'
          metricName: 'DeadletteredMessages'
          dimensions: [
            {
              name: 'EntityName'
              operator: 'Include'
              values: [invoiceExtractionQueueName]
            }
          ]
          // A count of messages waiting, not of events: the maximum in the window
          timeAggregation: 'Maximum'
          operator: 'GreaterThan'
          threshold: 0
        }
      ]
    }
    actions: [
      {
        actionGroupId: emailMe.id
      }
    ]
  }
}
