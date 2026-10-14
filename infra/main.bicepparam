using 'main.bicep'

// Immutable tags: the deployment always says exactly which commit is running
param apiImage = 'ghcr.io/rlopezb17/plumy-api:sha-5930d31'
param webImage = 'ghcr.io/rlopezb17/plumy-web:sha-5930d31'

// Per-person values come from the environment (see infra/README.md), so they are not hardcoded in the repo
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
param clientIpAddress = readEnvironmentVariable('CLIENT_IP', '')
