# Infrastructure (Bicep)

Everything Plumy needs in Azure, declared in `main.bicep`:

| Resource | Name | Notes |
|---|---|---|
| Log Analytics workspace | `log-plumy` | Container logs, 30-day retention, 0.1 GB/day cap |
| User-assigned managed identity | `id-plumy-api` | The API's identity for Azure SQL (no passwords) |
| Azure SQL server + database | `sql-plumy-<unique>` / `plumydb` | Serverless free offer, Entra ID auth only, auto-pauses when the free limit runs out |
| Container Apps environment | `cae-plumy` | Consumption profile, scale to zero |
| Container App (API) | `ca-plumy-api` | Internal ingress only, liveness `/alive`, readiness `/health` |
| Container App (web) | `ca-plumy-web` | Public ingress; nginx serves Angular and proxies `/api` to the API |
| Storage account + container | `stplumy<unique>` / `invoices` | Uploaded invoices. Entra ID only (no keys, no SAS, no public blobs), 7-day soft delete. The API's identity is *Storage Blob Data Contributor* on the account; the worker's, *Storage Blob Data Reader* on the container |
| Service Bus namespace + queue | `sb-plumy-<unique>` / `invoice-extraction` | Basic tier, Entra ID only (local auth disabled). The API is *Data Sender* on the queue, the worker *Data Owner* on it (KEDA reads its length). See [ADR 0003](../docs/adr/0003-invoice-extraction-in-a-background-worker.md) |
| Document Intelligence account | `di-plumy-<unique>` | Reads the invoices (prebuilt invoice model). Free tier F0: 500 pages a month, the first 2 pages of each file, files up to 4 MB, one F0 account per subscription. Entra ID only (local auth disabled); the worker is *Cognitive Services User* on it. See [ADR 0004](../docs/adr/0004-read-invoices-with-document-intelligence.md) |
| Foundry account + model deployment | `aif-plumy-<unique>` / `gpt-5.4-mini` | Suggests the ledger account of invoices from new suppliers. AIServices S0, Data Zone Standard (requests stay in the EU), 10K tokens a minute as a ceiling, paid per token. Entra ID only (local auth disabled); the worker is *Cognitive Services User* on it. See [ADR 0005](../docs/adr/0005-suggest-ledger-accounts-with-rules-then-a-language-model.md) |
| User-assigned managed identity | `id-plumy-worker` | The worker's identity: SQL, blobs (read only), the queue, Document Intelligence and Foundry |
| Container App (worker) | `ca-plumy-worker` | No ingress. Scales 0–2 on the queue's length (KEDA `azure-servicebus`, signs in with `id-plumy-worker`) |
| Application Insights | `appi-plumy` | Traces, metrics and logs from the API and the worker (OpenTelemetry). Stored in `log-plumy`, local auth disabled: both identities are *Monitoring Metrics Publisher* on it |
| Key Vault | `kv-plumy-<unique>` | Stripe's API key and webhook secret, the only secrets Entra ID can't replace. Azure RBAC, 7-day soft delete; the API's identity is *Key Vault Secrets User* on it. The secrets are set by hand: see [Payments](#payments-stripe) |
| Action group + metric alerts | `ag-plumy-email`, `alert-plumy-api-5xx`, `alert-plumy-dead-letters` | Email when the API answers a 5xx or a message lands in the dead-letter queue. See [Observability](#observability) |

The images come from GHCR (public packages, so no registry credentials). Always deploy an immutable `sha-xxxxxxx` tag.

## Manual deployment

Prerequisites: Azure CLI logged in (`az login`), Bicep (`az bicep install`), the .NET SDK and `dotnet tool restore`.

### 1. Resource group

```bash
az group create --name rg-plumy --location francecentral --tags app=plumy
```

France Central, not Spain Central: the Azure SQL free offer is not available in Spain Central
(`ProvisioningDisabled: Provisioning of free limit database is not supported for provided service level objective or region`).
Everything stays in one region so the API does not cross Europe on every query.

### 2. Parameters

The SQL admin is the Entra group `sg-plumy-sql-admins` (the pipeline identity, see
[Continuous deployment](#continuous-deployment)). Deploying by hand needs you in it for steps 4 and 5: see
[Access for people](#access-for-people). `main.bicepparam` reads these from the environment:

```bash
export IMAGE_TAG=sha-$(git rev-parse --short=7 origin/main)   # an image the publish job already pushed
export SQL_ADMIN_LOGIN=sg-plumy-sql-admins
export SQL_ADMIN_OBJECT_ID=$(az ad group show --group sg-plumy-sql-admins --query id -o tsv)
export CLIENT_IP=$(curl -s https://api.ipify.org)   # opens the SQL firewall to your machine only
export ALERT_EMAIL=$(az account show --query user.name -o tsv)   # where the alerts go
```

### 3. Preview, then deploy

```bash
az deployment group what-if --name main --resource-group rg-plumy --parameters infra/main.bicepparam
az deployment group create  --name main --resource-group rg-plumy --parameters infra/main.bicepparam
```

Deployments are idempotent: running `create` again only changes what differs from the template.
Expect some noise in `what-if`: defaults Azure fills in show up as `Delete`, and values that depend on other
resources (`reference(...)`) show up as `Modify` even when they will not change.

`--name main` matters: the later steps read the outputs from the deployment called `main`.

### 4. Apply the migrations

The database starts empty. Build the migration bundle and run it with your own Entra ID identity.
`Active Directory Default` tries a chain of credentials; on a laptop the managed identity probe hangs until the
connection times out, so `AZURE_TOKEN_CREDENTIALS` limits the chain to the `az login` session:

```bash
dotnet ef migrations bundle --project src/Plumy.Infrastructure --startup-project src/Plumy.Api \
  --configuration Release --self-contained -r linux-x64 --output efbundle --force
git checkout -- 'src/*/packages.lock.json'   # building for linux-x64 adds a RID section to the lock files

SQL_FQDN=$(az deployment group show -g rg-plumy -n main --query properties.outputs.sqlServerFqdn.value -o tsv)
export AZURE_TOKEN_CREDENTIALS=AzureCliCredential
./efbundle --connection "Server=tcp:$SQL_FQDN,1433;Database=plumydb;Authentication=Active Directory Default;Encrypt=True"
```

### 5. Give the API access to the database

Bicep cannot create database users. `scripts/grant-sql-access.cs` (a .NET 10 file-based app) creates the user for
the API's managed identity and adds it to `db_datareader` and `db_datawriter`. It is idempotent, and it runs as
you, a SQL admin (same `AZURE_TOKEN_CREDENTIALS` as above):

```bash
API_CLIENT_ID=$(az deployment group show -g rg-plumy -n main --query properties.outputs.apiIdentityClientId.value -o tsv)
dotnet run infra/scripts/grant-sql-access.cs -- "$SQL_FQDN" plumydb id-plumy-api "$API_CLIENT_ID" identity readwrite
```

It creates the user `WITH SID` (the identity's client ID) instead of `FROM EXTERNAL PROVIDER`, so the SQL server
never has to look the identity up in Microsoft Graph: that lookup fails when the caller is a service principal.

The worker needs the same, with its own identity (it saves each invoice's status and data):

```bash
WORKER_CLIENT_ID=$(az deployment group show -g rg-plumy -n main --query properties.outputs.workerIdentityClientId.value -o tsv)
dotnet run infra/scripts/grant-sql-access.cs -- "$SQL_FQDN" plumydb id-plumy-worker "$WORKER_CLIENT_ID" identity readwrite
```

And the readers group, read only (it uses the group's object ID as SID, `TYPE = X`):

```bash
READERS_ID=$(az ad group show --group sg-plumy-readers --query id -o tsv)
dotnet run infra/scripts/grant-sql-access.cs -- "$SQL_FQDN" plumydb sg-plumy-readers "$READERS_ID" group read
```

Least privilege: the API can read and write data but not change the schema. Only migrations do that.
Until this runs, the API's readiness probe fails (the SQL login is rejected) and the web gets a 504 on `/api`.

### 6. Open the app

```bash
az deployment group show -g rg-plumy -n main --query properties.outputs.webUrl.value -o tsv
```

The first request after a while idle is slow: the web, the API and the database all start from zero.

## Custom domain

The web is served at `https://app.getplumy.com` (`webCustomDomain` in `main.bicepparam`) with a free managed
certificate that Azure issues and renews. The DNS zone is in Cloudflare, and these two records must exist **before**
the deployment that adds the domain, because Azure validates them while it issues the certificate:

| Type | Name | Content | Proxy |
|---|---|---|---|
| `CNAME` | `app` | the web's default FQDN (output `webDefaultFqdn`) | DNS only (grey cloud) |
| `TXT` | `asuid.app` | the environment's verification ID (output `domainVerificationId`) | — |

```bash
az deployment group show -g rg-plumy -n main --query "properties.outputs.[webDefaultFqdn.value, domainVerificationId.value]" -o tsv
```

DNS only, not proxied: with the orange cloud, Cloudflare answers with its own IPs and TLS certificate, so Azure can't
validate the CNAME or issue its certificate.

After a tear down the new environment gets a different default domain (`<random>.francecentral.azurecontainerapps.io`),
so the `CNAME` has to be updated before the first deployment, or the certificate step fails. To recreate everything
from scratch, either update the record first (create the environment with `webCustomDomain=''`, read the outputs,
fix DNS, deploy again) or deploy without the domain.

## Continuous deployment

Every push to `main` deploys: the `deploy` job in `.github/workflows/ci.yml` runs after the images are published
and does steps 3 to 5 above with the commit's `sha-xxxxxxx` images, then a smoke test through the public URL.
It covers a first deployment from an empty resource group too.

It logs in to Azure with OIDC (workload identity federation), so there is no Azure secret in GitHub: the job asks
GitHub for a signed token, and Entra ID exchanges it for an Azure token because the identity `id-plumy-github`
has a federated credential that trusts exactly `repo:chelomama-dev@<owner id>/plumy@<repo id>:environment:production` (GitHub's immutable subject format).

One-off setup (and again after deleting `rg-plumy`, which also deletes the role assignment):

```bash
infra/scripts/bootstrap-github-oidc.sh
```

| Created | Where | Why |
|---|---|---|
| Resource providers (`Microsoft.App`, `.ServiceBus`, `.Sql`, `.Storage`…) registered | Subscription | A new subscription can't create those resources until they are registered, and the pipeline has no subscription-wide rights to do it |
| `rg-plumy` | Azure | The app; the pipeline is Contributor here and nowhere else |
| Role Based Access Control Administrator on `rg-plumy`, with a condition | Azure | Lets the pipeline assign the data roles Bicep needs (*Storage Blob Data Contributor* and *Reader*, *Azure Service Bus Data Sender* and *Owner*, *Cognitive Services User*) to service principals, and no other role. Run the script again when that list changes |
| `id-plumy-github` + federated credential | `rg-plumy-cicd` | The pipeline's identity, outside `rg-plumy` so it survives a tear down |
| `sg-plumy-sql-admins` (`id-plumy-github` only) | Entra ID | The SQL server's admin. The script never adds you: it warns if you are still in it |
| `sg-plumy-readers` (you) + *Storage Blob Data Reader* on `rg-plumy` | Entra ID / Azure | Day-to-day access for people, read only. The deployment makes it `db_datareader` |
| `ag-plumy-subscription` + `alert-plumy-service-health` | `rg-plumy-cicd` | Emails you about Azure incidents and maintenance that affect the subscription. Subscription-wide, so not in Bicep |
| Environment `production` (deploys from `main` only) + variables | GitHub | Client, tenant and subscription IDs, the groups and the alert email: identifiers, not secrets |

## Access for people

Day to day you are a reader: you can query the database and open the invoice files, but not change them. Being an
admin is something you switch on for one task (a manual migration, fixing data by hand) and off again, the poor
man's version of Entra Privileged Identity Management (which needs a paid licence).

**Query the database** (VS Code, MSSQL extension). SQL only accepts Entra ID, so there is no SQL user or password:

1. The firewall must allow your IP: `az sql server firewall-rule create -g rg-plumy -s <sql-plumy-…> -n dev-home
   --start-ip-address <ip> --end-ip-address <ip>` (`curl -s https://api.ipify.org`). Deleting `rg-plumy` deletes it.
2. Server `<sql-plumy-…>.database.windows.net`, database `plumydb`, authentication *Microsoft Entra ID - Universal
   with MFA support*, your account and the subscription's tenant (as a guest, the default tenant is the wrong one).
3. Check who you are: `SELECT USER_NAME(), IS_ROLEMEMBER('db_datareader'), IS_ROLEMEMBER('db_owner');` A reader gets
   `1, 0`. An admin is `dbo` and gets `1, 1`.

**Open the invoice files**: portal → storage account → Storage browser → `invoices`, authentication *Microsoft
Entra user account*. Or `az storage blob list --account-name <stplumy…> -c invoices --auth-mode login -o table`.

**Become a SQL admin for a while**:

```bash
ME=$(az ad signed-in-user show --query id -o tsv)
az ad group member add --group sg-plumy-sql-admins --member-id "$ME"
# Sign out and in again in the MSSQL extension: group membership travels in the token
# ... the task ...
az ad group member remove --group sg-plumy-sql-admins --member-id "$ME"
```

## Observability

The API and the worker send OpenTelemetry data (traces, metrics, logs) to **Application Insights** when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is set (`Plumy.ServiceDefaults`, Azure Monitor distro). Locally it is not
set, and the same data goes to the Aspire dashboard through OTLP. Keyless: the connection string only says where
to send, and each app signs in with its managed identity.

Where to look (portal → `appi-plumy`):

- **Application map**: web → API → SQL / Blob / Service Bus → worker → Document Intelligence, with failure rates.
- **Transaction search** / **Failures**: one upload followed end to end, from the API request to the worker
  reading the invoice (the Service Bus message carries the trace context).
- **Logs** (KQL, same workspace as the console logs):

```kusto
// Slowest and failing API operations, last 24 h
AppRequests
| where TimeGenerated > ago(24h)
| summarize count(), failures = countif(Success == false), p95 = percentile(DurationMs, 95) by Name
| order by failures desc, p95 desc

// Exceptions with the message, to go from an alert to the cause
AppExceptions
| where TimeGenerated > ago(24h)
| project TimeGenerated, AppRoleName, ProblemId, OuterMessage, OperationId
```

Alerts (email to `ALERT_EMAIL`, set by the bootstrap):

| Alert | Fires when | Where |
|---|---|---|
| `alert-plumy-api-5xx` | The API answers any 5xx in 5 minutes (Container Apps `Requests` metric) | Bicep |
| `alert-plumy-dead-letters` | A message is in the `invoice-extraction` dead-letter queue | Bicep |
| `alert-plumy-service-health` | Azure has an incident or maintenance that affects the subscription | Bootstrap |

Metric alerts cost about $0.10 a month each; activity log alerts are free. The telemetry counts against the
workspace's 0.1 GB/day cap: if it is reached, ingestion stops until the next day (the apps keep working).

## Payments (Stripe)

The API takes payments with Stripe ([ADR 0012](../docs/adr/0012-plans-with-a-monthly-invoice-limit.md)), in a
sandbox. Its two secrets live in Key Vault; the API's container app reads them with its managed identity and gets
them as `Stripe__SecretKey` and `Stripe__WebhookSecret`. Bicep creates the vault but never the secrets, so their
values aren't in the repo, the pipeline or the deployment history.

While `stripeEnabled` is `false` (`main.bicepparam`) the API runs without them and payments answer 503. To turn
them on, once:

1. **Let yourself write secrets** in the vault (reading them is all the API can do):

   ```bash
   VAULT=$(az keyvault list -g rg-plumy --query "[0].name" -o tsv)
   az role assignment create --role "Key Vault Secrets Officer" \
     --assignee "$(az ad signed-in-user show --query id -o tsv)" \
     --scope "$(az keyvault show -n "$VAULT" --query id -o tsv)"
   ```

2. **A restricted key** in the Stripe dashboard (Developers → API keys → Create restricted key), sandbox, with only:
   Customers *Write*, Checkout Sessions *Write*, Customer portal *Write*, Subscriptions *Read*, Prices *Read*.
   If it leaks it can't refund, pay out or read cards. The API refuses live keys.
3. **The webhook endpoint** in Stripe. The answer's `secret` (`whsec_…`) is the signing secret:

   ```bash
   stripe webhook_endpoints create --url https://app.getplumy.com/api/billing/stripe-webhook \
     -d "enabled_events[]=checkout.session.completed" \
     -d "enabled_events[]=customer.subscription.created" \
     -d "enabled_events[]=customer.subscription.updated" \
     -d "enabled_events[]=customer.subscription.deleted" \
     -d "enabled_events[]=customer.subscription.paused" \
     -d "enabled_events[]=customer.subscription.resumed"
   ```

4. **Both secrets into the vault.** `read -rs` asks for the value without echoing it or leaving it in the shell
   history:

   ```bash
   read -rsp "Stripe restricted key: " v && az keyvault secret set --vault-name "$VAULT" \
     --name stripe-secret-key --value "$v" --output none; unset v; echo
   read -rsp "Webhook signing secret: " v && az keyvault secret set --vault-name "$VAULT" \
     --name stripe-webhook-secret --value "$v" --output none; unset v; echo
   ```

5. **`stripeEnabled = true`** in `main.bicepparam`, through a pull request. A revision that references a secret
   missing from the vault never starts, which is why the switch waits for step 4.

Rotating a secret is step 4 again: Container Apps picks up the new version within 30 minutes, or straight away
with `az containerapp revision restart`. The Prices (`plumy_basic_monthly`, `plumy_pro_monthly`) and the customer
portal settings are set up as for local development ([dev-setup](../docs/dev-setup.md)).

## Identity (Entra External ID)

Users sign in with an Entra External ID tenant (see [ADR 0001](../docs/adr/0001-authentication-with-entra-external-id.md)).
It is set up once, by hand, and lives outside `rg-plumy` so tearing down the app never deletes the users.

| | |
|---|---|
| Tenant | `plumy.onmicrosoft.com` (`a228e3ce-b747-42f6-9092-db0b378d4da0`), data in Europe |
| Billing resource | `rg-plumy-identity` |
| Authority | `https://a228e3ce-b747-42f6-9092-db0b378d4da0.ciamlogin.com/a228e3ce-b747-42f6-9092-db0b378d4da0/v2.0` |

1. Tenant: Entra admin center → Manage tenants → Create → External, location Europe, resource group
   `rg-plumy-identity`.
2. User flow, in the external tenant: External Identities → User flows → New user flow `signup-signin`,
   identity provider *Email with password*, attributes *Email Address* and *Display Name*.
3. App registrations (`plumy-api` with the `access_as_user` scope, `plumy-web` as SPA) and admin consent:

   ```bash
   az login --tenant a228e3ce-b747-42f6-9092-db0b378d4da0 --allow-no-subscriptions
   az account set --subscription <your subscription>   # back to the usual one
   infra/scripts/bootstrap-entra-apps.sh
   ```

   The script also links `plumy-web` to the user flow.

## Tear down

```bash
az group delete --name rg-plumy
```

Deletes everything above in one go. A deleted free-offer database frees its slot within an hour.
`rg-plumy-cicd` and the Entra groups stay; run the bootstrap again before the next deployment.

The Key Vault is kept soft-deleted for 7 days and blocks a vault with the same name; purge it with
`az keyvault purge --name <vault>` before deploying again.

The Document Intelligence and Foundry accounts are not gone yet: Azure keeps them soft-deleted for 48 hours, and
while they are there a new deployment fails (same names, and Document Intelligence still takes the subscription's
only F0 slot). Purge both before deploying again:

```bash
az cognitiveservices account list-deleted --query "[].{name:name, location:location}" -o table
az cognitiveservices account purge --location francecentral --resource-group rg-plumy --name <di-plumy-…>
az cognitiveservices account purge --location francecentral --resource-group rg-plumy --name <aif-plumy-…>
```
