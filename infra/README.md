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

The SQL admin is the Entra group `sg-plumy-sql-admins` (you and the pipeline identity, see
[Continuous deployment](#continuous-deployment)). `main.bicepparam` reads these from the environment:

```bash
export IMAGE_TAG=sha-$(git rev-parse --short=7 origin/main)   # an image the publish job already pushed
export SQL_ADMIN_LOGIN=sg-plumy-sql-admins
export SQL_ADMIN_OBJECT_ID=$(az ad group show --group sg-plumy-sql-admins --query id -o tsv)
export CLIENT_IP=$(curl -s https://api.ipify.org)   # opens the SQL firewall to your machine only
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
dotnet run infra/scripts/grant-sql-access.cs -- "$SQL_FQDN" plumydb id-plumy-api "$API_CLIENT_ID"
```

It creates the user `WITH SID` (the identity's client ID) instead of `FROM EXTERNAL PROVIDER`, so the SQL server
never has to look the identity up in Microsoft Graph: that lookup fails when the caller is a service principal.

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
has a federated credential that trusts exactly `repo:rlopezb17/plumy:environment:production`.

One-off setup (and again after deleting `rg-plumy`, which also deletes the role assignment):

```bash
infra/scripts/bootstrap-github-oidc.sh
```

| Created | Where | Why |
|---|---|---|
| `rg-plumy` | Azure | The app; the pipeline is Contributor here and nowhere else |
| `id-plumy-github` + federated credential | `rg-plumy-cicd` | The pipeline's identity, outside `rg-plumy` so it survives a tear down |
| `sg-plumy-sql-admins` (you + `id-plumy-github`) | Entra ID | The SQL server's admin: one admin slot, two admins |
| Environment `production` (deploys from `main` only) + variables | GitHub | Client, tenant and subscription IDs and the group: identifiers, not secrets |

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
`rg-plumy-cicd` and the Entra group stay; run the bootstrap again before the next deployment.
