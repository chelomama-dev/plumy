// Container Apps environment + the three apps. Only the web is public; it proxies /api to the internal API (nginx).
// The worker has no ingress at all: it only takes messages from Service Bus.
param location string
param appName string
param tags object
param workspaceName string
param apiImage string
param webImage string
param workerImage string
@description('Custom domain for the web (e.g. app.getplumy.com). Empty = only the default *.azurecontainerapps.io name.')
param webCustomDomain string
param apiIdentityId string
param apiIdentityClientId string
param workerIdentityId string
param workerIdentityClientId string
param sqlServerFqdn string
param databaseName string
@description('Blob endpoint of the storage account, e.g. https://stplumyxxx.blob.core.windows.net/')
param blobEndpoint string
@description('Short name of the Service Bus namespace (KEDA wants the name, the apps the host name).')
param serviceBusNamespaceName string
@description('e.g. sb-plumy-xxx.servicebus.windows.net')
param serviceBusHostName string
param invoiceExtractionQueueName string
param invoiceExtractionEnabled bool
param invoiceExtractionMaxDeliveryCount int
@description('e.g. https://di-plumy-xxx.cognitiveservices.azure.com/')
param documentIntelligenceEndpoint string

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: 'cae-${appName}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        // Read at deploy time, never stored in the template or its outputs
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    // Consumption profile only: pay per second of use, scale to zero, monthly free grant
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

// No password: SqlClient asks the managed identity (selected by its client ID) for an Entra ID token.
// Connect Timeout covers the serverless database resuming from auto-pause (up to about a minute).
func sqlConnectionString(server string, database string, identityClientId string) string =>
  'Server=tcp:${server},1433;Database=${database};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;Connect Timeout=60'

var apiConnectionString = sqlConnectionString(sqlServerFqdn, databaseName, apiIdentityClientId)
var workerConnectionString = sqlConnectionString(sqlServerFqdn, databaseName, workerIdentityClientId)

resource api 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'ca-${appName}-api'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      // Internal: only reachable from inside the environment (the web's nginx), never from the internet
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            // Same key the Aspire AppHost injects locally (ConnectionStrings:plumydb)
            {
              name: 'ConnectionStrings__plumydb'
              value: apiConnectionString
            }
            // Only the URL, no key: given a URL, the Aspire Blob client signs in with an Azure.Identity credential
            {
              name: 'ConnectionStrings__blobs'
              value: blobEndpoint
            }
            // Host name only, like the blobs: the Service Bus client signs in with the managed identity
            {
              name: 'ConnectionStrings__messaging'
              value: serviceBusHostName
            }
            // Off: uploads are saved but not sent to the worker (they stay Pending)
            {
              name: 'InvoiceExtraction__Enabled'
              value: string(invoiceExtractionEnabled)
            }
            // The container app could have several user-assigned identities: this says which one to use
            {
              name: 'AZURE_CLIENT_ID'
              value: apiIdentityClientId
            }
            // Go straight to the managed identity instead of trying the whole DefaultAzureCredential chain
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
          probes: [
            // Restart only if the process itself is stuck
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 10
            }
            // Stop sending traffic while the database is unreachable (includes the SQL health check).
            // The default 1 s timeout is too short for the first connection (managed identity token + TLS), and
            // the probe cancels the health check when it gives up, so the connection never finished opening.
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              timeoutSeconds: 10
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'ca-${appName}-web'
  location: location
  tags: tags
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // 'Auto' binds the managed certificate below as soon as it is issued, so domain and certificate go in
        // one deployment (older API versions needed three: Disabled binding → certificate → SniEnabled)
        customDomains: empty(webCustomDomain)
          ? []
          : [
              {
                name: webCustomDomain
                bindingType: 'Auto'
              }
            ]
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            // Read by nginx.conf.template at startup. HTTPS: internal ingress also terminates TLS.
            {
              name: 'API_URL'
              value: 'https://${api.properties.configuration.ingress.fqdn}'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 8080
              }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// Reads uploaded invoices from the queue. No ingress and no probes: it serves no HTTP. KEDA starts replicas when
// messages are waiting and removes them when the queue is empty, so it costs nothing while idle
resource worker 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'ca-${appName}-worker'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentityId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ConnectionStrings__plumydb'
              value: workerConnectionString
            }
            {
              name: 'ConnectionStrings__blobs'
              value: blobEndpoint
            }
            {
              name: 'ConnectionStrings__messaging'
              value: serviceBusHostName
            }
            {
              name: 'InvoiceExtraction__Extractor'
              value: 'DocumentIntelligence'
            }
            {
              name: 'InvoiceExtraction__DocumentIntelligence__Endpoint'
              value: documentIntelligenceEndpoint
            }
            {
              name: 'InvoiceExtraction__MaxDeliveryCount'
              value: string(invoiceExtractionMaxDeliveryCount)
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: workerIdentityClientId
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules: [
          {
            name: 'invoice-extraction-queue'
            custom: {
              // KEDA's Service Bus scaler: one replica per 5 waiting messages (rounded up), none when it's empty
              type: 'azure-servicebus'
              metadata: {
                namespace: serviceBusNamespaceName
                queueName: invoiceExtractionQueueName
                messageCount: '5'
              }
              // Reads the queue's length as the worker's identity instead of with a connection string (the
              // namespace has no keys)
              identity: workerIdentityId
            }
          }
        ]
      }
    }
  }
}

// Free certificate issued and renewed by Azure. CNAME validation: the domain must already be a CNAME to the web's
// default FQDN, plus a TXT asuid.<domain> record with the environment's verification ID (see infra/README.md).
resource webCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2026-01-01' = if (!empty(webCustomDomain)) {
  parent: environment
  name: 'cert-${replace(webCustomDomain, '.', '-')}'
  location: location
  tags: tags
  properties: {
    subjectName: webCustomDomain
    domainControlValidation: 'CNAME'
  }
  // The hostname has to be on a container app before Azure issues a certificate for it
  dependsOn: [web]
}

output webUrl string = empty(webCustomDomain)
  ? 'https://${web.properties.configuration.ingress.fqdn}'
  : 'https://${webCustomDomain}'
output webDefaultFqdn string = web.properties.configuration.ingress.fqdn
output domainVerificationId string = environment.properties.customDomainConfiguration.customDomainVerificationId
output apiFqdn string = api.properties.configuration.ingress.fqdn
