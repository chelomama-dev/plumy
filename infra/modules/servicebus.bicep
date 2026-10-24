// Service Bus for the invoice extraction queue: the API sends, the worker receives. Entra ID only, like Storage.
param location string
@description('Globally unique, 6-50 letters, digits and hyphens.')
param namespaceName string
param tags object
param queueName string
@description('Must match the worker\'s InvoiceExtraction:MaxDeliveryCount.')
param maxDeliveryCount int
@description('Principal that sends to the queue (the API\'s managed identity).')
param senderPrincipalId string
@description('Principal that receives from it and that KEDA uses to read its length (the worker\'s identity).')
param receiverPrincipalId string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  // Basic: queues and dead-letter queues, billed per operation, no base fee. No topics, sessions or duplicate
  // detection, which this queue doesn't use (the worker is idempotent instead)
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    minimumTlsVersion: '1.2'
    // No SAS keys or connection strings: only Entra ID identities with a data role get in
    disableLocalAuth: true
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: queueName
  properties: {
    // A delivery that fails is retried when its lock expires, so this also spaces the retries out
    lockDuration: 'PT1M'
    // After this many deliveries Service Bus moves the message to the dead-letter queue
    maxDeliveryCount: maxDeliveryCount
    // The most the Basic tier allows
    defaultMessageTimeToLive: 'P14D'
  }
}

// Built-in roles. Scoped to the queue, not the namespace: each identity reaches this queue and nothing else
var dataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
// Owner and not Receiver: besides receiving, KEDA reads the queue's length through the management API, which the
// Receiver role doesn't cover. Still only this queue
var dataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

resource senderAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, senderPrincipalId, dataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dataSenderRoleId)
    principalId: senderPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource receiverAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, receiverPrincipalId, dataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dataOwnerRoleId)
    principalId: receiverPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output namespaceName string = namespace.name
output namespaceId string = namespace.id
// What the apps get as ConnectionStrings__messaging: a host name, so the client signs in with the managed identity
output hostName string = '${namespace.name}.servicebus.windows.net'
