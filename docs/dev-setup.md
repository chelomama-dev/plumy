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

SQL Server is not installed locally: Aspire runs it in a container.

## Run

```bash
aspire run   # SQL Server + API + Angular (web/). Aspire runs `pnpm install` before starting the web
```

The Angular dev server proxies `/api` to the API (`web/proxy.conf.mjs`), so the API needs no CORS. To run the
front alone: `cd web && pnpm start` (expects the API on its https launch profile, https://localhost:7062).

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
