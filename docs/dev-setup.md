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

### Payments (Stripe), optional

Without Stripe keys the API runs with payments off: the trial and the limits work, and checkout answers 503. To try
paying, with a Stripe **sandbox** (the API refuses live keys):

1. Install the [Stripe CLI](https://docs.stripe.com/stripe-cli) and run `stripe login` once.
2. Create the two plans. The API finds them by lookup key, so the amounts can change in Stripe without a deploy:

   ```bash
   stripe prices create --currency eur --unit-amount 2900 -d "recurring[interval]=month" \
     -d "product_data[name]=Plumy Basic" -d lookup_key=plumy_basic_monthly
   stripe prices create --currency eur --unit-amount 9900 -d "recurring[interval]=month" \
     -d "product_data[name]=Plumy Pro" -d lookup_key=plumy_pro_monthly
   ```

3. Save the customer portal settings once in the dashboard (*Settings → Billing → Customer portal*), allowing plan
   changes between the two products. Without a saved configuration Stripe refuses to open the portal.
4. Put the keys in the AppHost's user secrets. The webhook secret is the one the CLI signs with; it doesn't change:

   ```bash
   dotnet user-secrets --project src/Plumy.AppHost set Stripe:SecretKey sk_test_…
   dotnet user-secrets --project src/Plumy.AppHost set Stripe:WebhookSecret "$(stripe listen --print-secret)"
   ```

`aspire run` then also starts `stripe-webhooks` (`stripe listen`), which forwards the sandbox's events to the API.
Pay with the test card `4242 4242 4242 4242`, any future date and any CVC. `stripe trigger customer.subscription.updated`
sends an event by hand, but for a customer Plumy doesn't know: the API logs it and ignores it.

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
