#!/usr/bin/env bash
# Creates or updates Plumy's app registrations in the Entra External ID tenant. Safe to run again.
#
#   plumy-api  the API: exposes the access_as_user scope and issues v2 access tokens with the email claim
#   plumy-web  the Angular SPA: authorization code + PKCE, redirects to localhost and app.getplumy.com
#
# It also grants admin consent (users of an external tenant can't consent themselves) and, if the
# signup-signin user flow exists (it is created in the portal, see infra/README.md), links plumy-web to it.
#
# Prerequisite, once: az login --tenant <external tenant id> --allow-no-subscriptions
# (then `az account set --subscription <yours>` to go back to your usual subscription; this script asks
# for its own token for the external tenant, so it doesn't depend on the active account)
set -euo pipefail

TENANT_ID="${TENANT_ID:-a228e3ce-b747-42f6-9092-db0b378d4da0}"
API_NAME="plumy-api"
WEB_NAME="plumy-web"
WEB_REDIRECT_URIS='["http://localhost:4200","https://app.getplumy.com"]'
SCOPE_NAME="access_as_user"
USER_FLOW_NAME="signup-signin"

GRAPH="https://graph.microsoft.com/v1.0"
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"
# Delegated permission ids of Microsoft Graph (the same in every tenant)
GRAPH_OPENID="37f7f235-527c-4136-accd-4a02d197296e"
GRAPH_OFFLINE_ACCESS="7427e0e9-2fba-42fe-b0c0-848c9e6a8182"

TOKEN=$(az account get-access-token --tenant "$TENANT_ID" --resource-type ms-graph --query accessToken -o tsv)

graph() { # graph <method> <url> [extra az rest args, e.g. --body or --query]
  local method=$1 url=$2
  shift 2
  az rest --method "$method" --url "$url" --headers "Authorization=Bearer $TOKEN" "Content-Type=application/json" "$@"
}

# Upsert by uniqueName: creates the app the first time and updates it afterwards
upsert_app() { # upsert_app <uniqueName> <body>
  az rest --method PATCH --url "$GRAPH/applications(uniqueName='$1')" \
    --headers "Authorization=Bearer $TOKEN" "Content-Type=application/json" "Prefer=create-if-missing" \
    --body "$2" > /dev/null
  graph GET "$GRAPH/applications(uniqueName='$1')"
}

ensure_service_principal() { # ensure_service_principal <appId> -> prints the service principal id
  local id
  id=$(graph GET "$GRAPH/servicePrincipals?\$filter=appId eq '$1'" --query "value[0].id" -o tsv 2>/dev/null || true)
  if [[ -z "$id" ]]; then
    id=$(graph POST "$GRAPH/servicePrincipals" --body "{\"appId\":\"$1\"}" --query id -o tsv)
  fi
  echo "$id"
}

grant_consent() { # grant_consent <client sp id> <resource sp id> <space-separated scopes>
  local existing
  existing=$(graph GET "$GRAPH/oauth2PermissionGrants?\$filter=clientId eq '$1' and resourceId eq '$2'" \
    --query "value[0].id" -o tsv 2>/dev/null || true)
  if [[ -z "$existing" ]]; then
    graph POST "$GRAPH/oauth2PermissionGrants" --body \
      "{\"clientId\":\"$1\",\"resourceId\":\"$2\",\"consentType\":\"AllPrincipals\",\"scope\":\"$3\"}" > /dev/null
  else
    graph PATCH "$GRAPH/oauth2PermissionGrants/$existing" --body "{\"scope\":\"$3\"}" > /dev/null
  fi
}

echo "==> $API_NAME"
api=$(upsert_app "$API_NAME" "{\"displayName\":\"$API_NAME\",\"signInAudience\":\"AzureADMyOrg\"}")
api_app_id=$(jq -r .appId <<< "$api")
api_object_id=$(jq -r .id <<< "$api")

# The scope id must stay the same across runs: reuse it if the scope already exists
scope_id=$(jq -r --arg s "$SCOPE_NAME" '.api.oauth2PermissionScopes[] | select(.value == $s) | .id' <<< "$api")
[[ -z "$scope_id" ]] && scope_id=$(uuidgen)

graph PATCH "$GRAPH/applications/$api_object_id" --body "$(jq -n \
  --arg uri "api://$api_app_id" --arg id "$scope_id" --arg value "$SCOPE_NAME" '{
    identifierUris: [$uri],
    api: {
      requestedAccessTokenVersion: 2,
      oauth2PermissionScopes: [{
        id: $id, value: $value, type: "User", isEnabled: true,
        adminConsentDisplayName: "Access Plumy", adminConsentDescription: "Lets the app call the Plumy API as the signed-in user.",
        userConsentDisplayName: "Access Plumy", userConsentDescription: "Lets the app call the Plumy API on your behalf."
      }]
    },
    optionalClaims: { accessToken: [{ name: "email", essential: false }] }
  }')" > /dev/null
api_sp_id=$(ensure_service_principal "$api_app_id")

echo "==> $WEB_NAME"
web=$(upsert_app "$WEB_NAME" "$(jq -n --arg name "$WEB_NAME" --argjson uris "$WEB_REDIRECT_URIS" \
  --arg api "$api_app_id" --arg scope "$scope_id" \
  --arg graph "$GRAPH_APP_ID" --arg openid "$GRAPH_OPENID" --arg offline "$GRAPH_OFFLINE_ACCESS" '{
    displayName: $name,
    signInAudience: "AzureADMyOrg",
    spa: { redirectUris: $uris },
    requiredResourceAccess: [
      { resourceAppId: $api, resourceAccess: [{ id: $scope, type: "Scope" }] },
      { resourceAppId: $graph, resourceAccess: [{ id: $openid, type: "Scope" }, { id: $offline, type: "Scope" }] }
    ]
  }')")
web_app_id=$(jq -r .appId <<< "$web")
web_sp_id=$(ensure_service_principal "$web_app_id")

echo "==> Admin consent"
graph_sp_id=$(ensure_service_principal "$GRAPH_APP_ID")
grant_consent "$web_sp_id" "$api_sp_id" "$SCOPE_NAME"
grant_consent "$web_sp_id" "$graph_sp_id" "openid offline_access"

echo "==> User flow"
flow_id=$(graph GET "$GRAPH/identity/authenticationEventsFlows" \
  --query "value[?displayName=='$USER_FLOW_NAME'].id | [0]" -o tsv)
if [[ -z "$flow_id" ]]; then
  echo "    '$USER_FLOW_NAME' not found: create it in the portal and run this script again" >&2
else
  flow_apps="$GRAPH/identity/authenticationEventsFlows/$flow_id/conditions/applications/includeApplications"
  linked=$(graph GET "$flow_apps" --query "value[?appId=='$web_app_id'].appId | [0]" -o tsv)
  if [[ -z "$linked" ]]; then
    graph POST "$flow_apps" --body "$(jq -n --arg appId "$web_app_id" \
      '{"@odata.type": "#microsoft.graph.authenticationConditionApplication", appId: $appId}')" > /dev/null
  fi
fi

cat <<EOF

Done. Values for the API and the Angular app (they are identifiers, not secrets):
  Authority     https://$TENANT_ID.ciamlogin.com/$TENANT_ID/v2.0
  API client id $api_app_id
  API scope     api://$api_app_id/$SCOPE_NAME
  Web client id $web_app_id
EOF
