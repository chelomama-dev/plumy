# Development environment setup

Reference machine: Ubuntu 26.04 LTS (x86_64). IDEs: JetBrains Rider (backend), VS Code (frontend).

| Tool | Version | Install method |
|---|---|---|
| .NET SDK | 10.0.x | Ubuntu archive: `sudo apt install dotnet-sdk-10.0` |
| Docker Engine | 29.x | Docker official apt repository (`docker-ce`, `buildx`, `compose` plugins); user added to the `docker` group |
| Node.js | 24 LTS | [nvm](https://github.com/nvm-sh/nvm) (`nvm install 24 && nvm alias default 24`) |
| pnpm | 12.x | `npm install -g pnpm` (under nvm, no sudo). Exact version pinned in `web/package.json` (`packageManager`) |
| Angular CLI | 22.x | `npm install -g @angular/cli` (under nvm, no sudo), then `ng config -g cli.packageManager pnpm` |
| Aspire CLI | 13.x | `curl -sSL https://aspire.dev/install.sh \| bash` (installs to `~/.aspire/bin`) |

SQL Server and Blob Storage are not installed locally: Aspire runs them in containers (SQL Server and Azurite, the
Azure Storage emulator).

## Run

```bash
aspire run   # SQL Server + Azurite + API + Angular (web/). Aspire runs `pnpm install` before starting the web
```

The Angular dev server proxies `/api` to the API (`web/proxy.conf.mjs`), so the API needs no CORS. To run the
front alone: `cd web && pnpm start` (expects the API on its https launch profile, https://localhost:7062).

Locally the worker uses a fake reader (`InvoiceExtraction:Extractor = Fake` in its `appsettings.Development.json`):
every invoice comes out with the same sample data, and nothing is paid. To try the real Document Intelligence
account, sign in with `az login` (your user needs *Cognitive Services User* on the account) and set, in the
worker's user secrets:

```bash
dotnet user-secrets --project src/Plumy.Worker set InvoiceExtraction:Extractor DocumentIntelligence
dotnet user-secrets --project src/Plumy.Worker set InvoiceExtraction:DocumentIntelligence:Endpoint \
  https://<di-plumy-…>.cognitiveservices.azure.com/
```

If the first read hangs, start with `AZURE_TOKEN_CREDENTIALS=AzureCliCredential aspire run`: it makes the SDK go
straight to your `az login` instead of trying every credential type first.

pnpm does not run dependency install scripts unless they are listed in `allowBuilds` (`web/pnpm-workspace.yaml`).
If an install fails with `ERR_PNPM_IGNORED_BUILDS`, review the package before running `pnpm approve-builds <pkg>`.

## IDE setup

- **Rider**: verify the .NET SDK is detected (`/usr/lib/dotnet`) and install the Aspire plugin.
- **VS Code**: extensions `Angular.ng-template`, `dbaeumer.vscode-eslint`, `esbenp.prettier-vscode`. Launch it from a terminal (`code .`) so it inherits the nvm `PATH`.

## Verify

```bash
dotnet --list-sdks
docker run --rm hello-world
node -v
pnpm -v
ng version
aspire --version
```
