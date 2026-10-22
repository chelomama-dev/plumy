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
SQL_ADMIN_GROUP=sg-plumy-sql-admins

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

# A SQL server has a single Entra admin. A group lets both you and the pipeline be admins.
echo "==> SQL admin group ($SQL_ADMIN_GROUP)"
if ! az ad group show --group "$SQL_ADMIN_GROUP" --output none 2>/dev/null; then
  az ad group create --display-name "$SQL_ADMIN_GROUP" --mail-nickname "$SQL_ADMIN_GROUP" --output none
fi
GROUP_ID=$(az ad group show --group "$SQL_ADMIN_GROUP" --query id -o tsv)

# A just-created managed identity takes a while to replicate in Entra ID (Graph answers "does not exist"),
# so retry instead of failing on the first run.
add_member() {
  for attempt in {1..12}; do
    if [[ $(az ad group member check --group "$GROUP_ID" --member-id "$1" --query value -o tsv 2>/dev/null) == "true" ]] ||
       az ad group member add --group "$GROUP_ID" --member-id "$1" 2>/dev/null; then
      return 0
    fi
    echo "    $1 not visible in Entra ID yet, retrying in 10 s ($attempt/12)"
    sleep 10
  done
  echo "    $1 could not be added to $SQL_ADMIN_GROUP" >&2
  return 1
}
add_member "$(az ad signed-in-user show --query id -o tsv)"
add_member "$PRINCIPAL_ID"

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

echo "Done. Deploy identity client ID: $CLIENT_ID"
