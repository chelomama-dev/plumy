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

### 2. Per-person parameters

The SQL admin is you (Entra ID). `main.bicepparam` reads these from the environment:

```bash
export SQL_ADMIN_LOGIN=$(az ad signed-in-user show --query userPrincipalName -o tsv)
export SQL_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
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
you, the SQL admin (same `AZURE_TOKEN_CREDENTIALS` as above):

```bash
dotnet run infra/scripts/grant-sql-access.cs -- "$SQL_FQDN" plumydb id-plumy-api
```

Least privilege: the API can read and write data but not change the schema. Only migrations do that.
Until this runs, the API's readiness probe fails (the SQL login is rejected) and the web gets a 504 on `/api`.

### 6. Open the app

```bash
az deployment group show -g rg-plumy -n main --query properties.outputs.webUrl.value -o tsv
```

The first request after a while idle is slow: the web, the API and the database all start from zero.

## Tear down

```bash
az group delete --name rg-plumy
```

Deletes everything above in one go. A deleted free-offer database frees its slot within an hour.
