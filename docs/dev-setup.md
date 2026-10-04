# Development environment setup

Reference machine: Ubuntu 26.04 LTS (x86_64). IDEs: JetBrains Rider (backend), VS Code (frontend).

| Tool | Version | Install method |
|---|---|---|
| .NET SDK | 10.0.x | Ubuntu archive: `sudo apt install dotnet-sdk-10.0` |
| Docker Engine | 29.x | Docker official apt repository (`docker-ce`, `buildx`, `compose` plugins); user added to the `docker` group |
| Node.js | 24 LTS | [nvm](https://github.com/nvm-sh/nvm) (`nvm install 24 && nvm alias default 24`) |
| Angular CLI | 22.x | `npm install -g @angular/cli` (under nvm, no sudo) |
| Aspire CLI | 13.x | `curl -sSL https://aspire.dev/install.sh \| bash` (installs to `~/.aspire/bin`) |

SQL Server is not installed locally: Aspire runs it in a container.

## IDE setup

- **Rider**: verify the .NET SDK is detected (`/usr/lib/dotnet`) and install the Aspire plugin.
- **VS Code**: extensions `Angular.ng-template`, `dbaeumer.vscode-eslint`, `esbenp.prettier-vscode`. Launch it from a terminal (`code .`) so it inherits the nvm `PATH`.

## Verify

```bash
dotnet --list-sdks
docker run --rm hello-world
node -v
ng version
aspire --version
```
