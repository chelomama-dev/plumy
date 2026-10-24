#!/usr/bin/env bash
# One-off setup so GitHub Actions can deploy Plumy to Azure without any stored secret (OIDC workload identity
# federation). Idempotent: run it again after deleting rg-plumy, because that also deletes the role assignment.
#
# Usage: infra/scripts/bootstrap-github-oidc.sh
# Needs: az logged in as a subscription Owner (to assign roles) who can create Entra groups, and gh logged in
# as a repo admin (to create the environment and its variables).
set -euo pipefail

# Asked to GitHub, not hardcoded: renaming the account changes it, and with it the OIDC subject Azure trusts
REPO=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
ENVIRONMENT=production
LOCATION=francecentral
APP_RG=rg-plumy                    # what the pipeline deploys into (deleted and recreated freely)
CICD_RG=rg-plumy-cicd              # the pipeline's own identity: lives apart so deleting rg-plumy keeps it
DEPLOY_IDENTITY=id-plumy-github
SQL_ADMIN_GROUP=sg-plumy-sql-admins  # the pipeline only: people join it just for the task that needs it
READERS_GROUP=sg-plumy-readers       # people, day to day: read production data (SQL and invoice files)

# A subscription must register a resource provider before it can create that kind of resource. That is a
# subscription-wide action the pipeline can't do (it only has rights on rg-plumy), so do it here, once.
# Keep this list in step with the resource types in infra/ (Microsoft.Authorization is always registered).
PROVIDERS=(Microsoft.App Microsoft.CognitiveServices Microsoft.Insights Microsoft.ManagedIdentity
  Microsoft.OperationalInsights Microsoft.ServiceBus Microsoft.Sql Microsoft.Storage)
echo "==> Resource providers"
for provider in "${PROVIDERS[@]}"; do
  if [[ $(az provider show --namespace "$provider" --query registrationState -o tsv) != Registered ]]; then
    echo "    registering $provider (can take a few minutes)"
    az provider register --namespace "$provider" --wait --output none
  fi
done

echo "==> Resource groups"
az group create --name "$APP_RG" --location "$LOCATION" --tags app=plumy --output none
az group create --name "$CICD_RG" --location "$LOCATION" --tags app=plumy purpose=cicd --output none

echo "==> Deployment identity ($DEPLOY_IDENTITY)"
az identity create --name "$DEPLOY_IDENTITY" --resource-group "$CICD_RG" --location "$LOCATION" --output none
CLIENT_ID=$(az identity show -n "$DEPLOY_IDENTITY" -g "$CICD_RG" --query clientId -o tsv)
PRINCIPAL_ID=$(az identity show -n "$DEPLOY_IDENTITY" -g "$CICD_RG" --query principalId -o tsv)

# Trust GitHub's tokens, but only for jobs that run in the 'production' environment of this repo.
# The environment only accepts deployments from main, so a PR branch can never get an Azure token.
# The subject prefix is asked to GitHub, not guessed: repos created after 2026-07-15 use the immutable format
# repo:owner@ownerId/repo@repoId, so a deleted and recreated repo with the same name can't reuse this trust.
SUBJECT="$(gh api "repos/$REPO/actions/oidc/customization/sub" --jq .sub_claim_prefix):environment:$ENVIRONMENT"
echo "==> Federated credential ($SUBJECT)"
az identity federated-credential create \
  --name github-$ENVIRONMENT \
  --identity-name "$DEPLOY_IDENTITY" \
  --resource-group "$CICD_RG" \
  --issuer https://token.actions.githubusercontent.com \
  --subject "$SUBJECT" \
  --audiences api://AzureADTokenExchange \
  --output none

# Contributor on the app resource group only: it can create and change resources there, but not grant
# permissions (that needs Owner / User Access Administrator) or touch anything else in the subscription.
echo "==> Contributor on $APP_RG"
az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope "$(az group show -n "$APP_RG" --query id -o tsv)" \
  --output none

# Bicep gives the apps' managed identities access to Blob Storage, Service Bus and Document Intelligence (role
# assignments), which Contributor can't do. Role Based Access Control Administrator can, and the condition narrows
# it to the data roles below, for service principals only: without it the pipeline could make itself Owner. Add a role's GUID to
# DATA_ROLES when the infra needs another one, and run this script again.
BLOB_DATA_CONTRIBUTOR=ba92f5b4-2d11-453d-a403-e96b0029c9fe
BLOB_DATA_READER=2a2b9908-6ea1-4ae2-8e65-a410df84e7d1
SERVICE_BUS_DATA_SENDER=69a216fc-b8fb-44d8-bc22-1f3c2cd27a39
SERVICE_BUS_DATA_OWNER=090c5cfd-751d-490a-894a-3ce6f1109419
COGNITIVE_SERVICES_USER=a97b65f3-24c7-4388-baec-2e87135dc908
MONITORING_METRICS_PUBLISHER=3913510d-42f4-4e42-8a64-420c390055eb  # sends telemetry to Application Insights
DATA_ROLES="$BLOB_DATA_CONTRIBUTOR, $BLOB_DATA_READER, $SERVICE_BUS_DATA_SENDER, $SERVICE_BUS_DATA_OWNER, \
$COGNITIVE_SERVICES_USER, $MONITORING_METRICS_PUBLISHER"
RBAC_CONDITION="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR \
(@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$DATA_ROLES} \
AND @Request[Microsoft.Authorization/roleAssignments:PrincipalType] ForAnyOfAnyValues:StringEqualsIgnoreCase {'ServicePrincipal'})) \
AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR \
(@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$DATA_ROLES}))"
APP_RG_ID=$(az group show -n "$APP_RG" --query id -o tsv)
echo "==> Role Based Access Control Administrator on $APP_RG (only for the data roles)"
# One assignment per principal, role and scope: an existing one (maybe with an older condition) is replaced
EXISTING=$(az role assignment list --assignee "$PRINCIPAL_ID" --role "Role Based Access Control Administrator" \
  --scope "$APP_RG_ID" --query "[].id" -o tsv)
if [[ -n "$EXISTING" ]]; then
  az role assignment delete --ids $EXISTING --output none
fi
az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Role Based Access Control Administrator" \
  --scope "$APP_RG_ID" \
  --condition "$RBAC_CONDITION" \
  --condition-version 2.0 \
  --output none

# Prints the group's object ID, creating the group first if needed (the ID comes from the create itself: a
# just-created group may not be visible to a lookup yet)
ensure_group() {
  az ad group show --group "$1" --query id -o tsv 2>/dev/null ||
    az ad group create --display-name "$1" --mail-nickname "$1" --query id -o tsv
}

# A just-created managed identity or group takes a while to replicate in Entra ID (Graph answers "does not exist"),
# so retry instead of failing on the first run. Usage: add_member <group id> <member object id>
add_member() {
  for attempt in {1..12}; do
    if [[ $(az ad group member check --group "$1" --member-id "$2" --query value -o tsv 2>/dev/null) == "true" ]] ||
       az ad group member add --group "$1" --member-id "$2" 2>/dev/null; then
      return 0
    fi
    echo "    $2 not visible in Entra ID yet, retrying in 10 s ($attempt/12)"
    sleep 10
  done
  echo "    $2 could not be added to group $1" >&2
  return 1
}

ME=$(az ad signed-in-user show --query id -o tsv)

# A SQL server has a single Entra admin, so it is a group. Only the pipeline is a permanent member: it applies the
# migrations and creates the database users. You join it only for a task that needs it, then leave (infra/README.md).
echo "==> SQL admin group ($SQL_ADMIN_GROUP)"
GROUP_ID=$(ensure_group "$SQL_ADMIN_GROUP")
add_member "$GROUP_ID" "$PRINCIPAL_ID"
if [[ $(az ad group member check --group "$GROUP_ID" --member-id "$ME" --query value -o tsv) == "true" ]]; then
  echo "    WARNING: you are a SQL admin. Leave the group when you are done:"
  echo "    az ad group member remove --group $SQL_ADMIN_GROUP --member-id $ME"
fi

# Day-to-day access for people: the pipeline makes the group a db_datareader in the database (grant-sql-access.cs),
# and here it gets Storage Blob Data Reader, so the invoice files can be opened without being able to change them.
# On the resource group (not the storage account), because the account doesn't exist until the first deployment.
echo "==> Readers group ($READERS_GROUP)"
READERS_ID=$(ensure_group "$READERS_GROUP")
add_member "$READERS_ID" "$ME"
# A just-created group can fail with PrincipalNotFound for a while, even with --assignee-principal-type: retry
for attempt in {1..12}; do
  if az role assignment create \
       --assignee-object-id "$READERS_ID" \
       --assignee-principal-type Group \
       --role "Storage Blob Data Reader" \
       --scope "$APP_RG_ID" \
       --output none 2>/dev/null; then
    break
  fi
  if (( attempt == 12 )); then
    echo "    Storage Blob Data Reader could not be assigned to $READERS_GROUP" >&2
    exit 1
  fi
  echo "    $READERS_GROUP not visible to Azure RBAC yet, retrying in 10 s ($attempt/12)"
  sleep 10
done

# Alerts go to the signed-in user's email. The app's alerts are in Bicep (rg-plumy); this one watches Azure itself
# (outages and planned maintenance in the services and regions the subscription uses), which is a subscription-wide
# thing the pipeline can't see, so it lives here, in rg-plumy-cicd, and survives a tear down. Activity log alerts
# are free.
ALERT_EMAIL=$(az account show --query user.name -o tsv)
echo "==> Service Health alert (to $ALERT_EMAIL)"
ACTION_GROUP_ID=$(az monitor action-group create --resource-group "$CICD_RG" --name ag-plumy-subscription \
  --short-name plumy-sub --action email owner "$ALERT_EMAIL" --query id -o tsv)
if ! az monitor activity-log alert show --resource-group "$CICD_RG" --name alert-plumy-service-health \
     --output none 2>/dev/null; then
  az monitor activity-log alert create --resource-group "$CICD_RG" --name alert-plumy-service-health \
    --scope "/subscriptions/$(az account show --query id -o tsv)" \
    --condition category=ServiceHealth \
    --action-group "$ACTION_GROUP_ID" \
    --description "Azure incidents, maintenance and advisories that affect this subscription" \
    --output none
fi

echo "==> GitHub environment '$ENVIRONMENT' (deployments from main only)"
gh api --method PUT "repos/$REPO/environments/$ENVIRONMENT" \
  -F 'deployment_branch_policy[protected_branches]=false' \
  -F 'deployment_branch_policy[custom_branch_policies]=true' \
  --silent
if ! gh api "repos/$REPO/environments/$ENVIRONMENT/deployment-branch-policies" \
     --jq '.branch_policies[].name' | grep -qx main; then
  gh api --method POST "repos/$REPO/environments/$ENVIRONMENT/deployment-branch-policies" \
    -f name=main -f type=branch --silent
fi

# Variables, not secrets: these are identifiers, useless without a token that only GitHub can issue
echo "==> Environment variables"
gh variable set AZURE_CLIENT_ID       --env "$ENVIRONMENT" --repo "$REPO" --body "$CLIENT_ID"
gh variable set AZURE_TENANT_ID       --env "$ENVIRONMENT" --repo "$REPO" --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID --env "$ENVIRONMENT" --repo "$REPO" --body "$(az account show --query id -o tsv)"
gh variable set SQL_ADMIN_GROUP_NAME  --env "$ENVIRONMENT" --repo "$REPO" --body "$SQL_ADMIN_GROUP"
gh variable set SQL_ADMIN_GROUP_ID    --env "$ENVIRONMENT" --repo "$REPO" --body "$GROUP_ID"
gh variable set SQL_READER_GROUP_NAME --env "$ENVIRONMENT" --repo "$REPO" --body "$READERS_GROUP"
gh variable set SQL_READER_GROUP_ID   --env "$ENVIRONMENT" --repo "$REPO" --body "$READERS_ID"
gh variable set ALERT_EMAIL           --env "$ENVIRONMENT" --repo "$REPO" --body "$ALERT_EMAIL"

echo "Done. Deploy identity client ID: $CLIENT_ID"
