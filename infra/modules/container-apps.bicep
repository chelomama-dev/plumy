// Container Apps environment + the two apps. Only the web is public; it proxies /api to the internal API (nginx).
param location string
param appName string
param tags object
param workspaceName string
param apiImage string
param webImage string
param apiIdentityId string
param apiIdentityClientId string
param sqlServerFqdn string
param databaseName string

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
var apiConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${apiIdentityClientId};Encrypt=True;Connect Timeout=60'

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

output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output apiFqdn string = api.properties.configuration.ingress.fqdn
