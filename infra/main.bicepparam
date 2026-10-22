using 'main.bicep'

// Immutable tags (sha-xxxxxxx): the deployment always says exactly which commit is running.
// The pipeline sets IMAGE_TAG to the commit it just built; by hand, see infra/README.md.
var imageTag = readEnvironmentVariable('IMAGE_TAG')
param apiImage = 'ghcr.io/chelomama-dev/plumy-api:${imageTag}'
param webImage = 'ghcr.io/chelomama-dev/plumy-web:${imageTag}'

param webCustomDomain = 'app.getplumy.com'

// The SQL admin is the Entra group sg-plumy-sql-admins (you + the pipeline identity), created by
// scripts/bootstrap-github-oidc.sh. The values come from the environment so no ids are hardcoded in the repo.
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
param sqlAdminPrincipalType = readEnvironmentVariable('SQL_ADMIN_PRINCIPAL_TYPE', 'Group')
param clientIpAddress = readEnvironmentVariable('CLIENT_IP', '')
