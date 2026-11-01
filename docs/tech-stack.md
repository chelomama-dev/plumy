# Tech stack & roadmap

Status: accepted (2026-10-04). The product idea is still to be defined.

## Core

| Area | Choice | Notes |
|---|---|---|
| Backend | .NET 10 (C#), ASP.NET Core Web API | EF Core as ORM |
| Frontend | Angular | |
| Database | SQL Server (Azure SQL Database in the cloud) | |
| Architecture | Clean Architecture (light) | 4 projects: Domain, Application, Infrastructure, Api. No MediatR (commercial license since 2025): own handlers |
| Local orchestration | Aspire 13 | SQL Server, Azurite, Service Bus emulator, API, worker and Angular with one command, built-in telemetry dashboard |
| Containers | Docker | From day 1 |

## Cloud & DevOps

| Area | Choice | Notes |
|---|---|---|
| Hosting | Azure Container Apps | Kubernetes-based, scale to zero, free monthly grant. AKS is not used initially (always-on node cost) |
| Messaging | Azure Service Bus (Basic) | Upload → queue → worker that reads the invoice, scaled by KEDA. Emulator locally. See [ADR 0003](adr/0003-invoice-extraction-in-a-background-worker.md) |
| CI/CD | GitHub Actions | Build, tests, Docker image, deploy |
| IaC | Bicep | No resources created manually in the portal |
| Secrets | Managed Identity, local auth disabled everywhere | SQL is Entra ID only, Storage has shared keys off, the AI services and Service Bus reject keys, GitHub deploys with OIDC. The only secrets are Stripe's API key and webhook secret: in Key Vault, read by the API's managed identity through Container Apps secret references, set by hand (never in the repo or the pipeline) |
| Observability | OpenTelemetry + Application Insights | |

## Product features

| Area | Choice | Notes |
|---|---|---|
| Auth | Microsoft Entra External ID | OIDC + JWT bearer. Tenancy and roles in our database. See [ADR 0001](adr/0001-authentication-with-entra-external-id.md) |
| Caching | Not yet | HybridCache (then Redis as L2) when there's a measured need; today no query is hot enough to justify it |
| AI | Azure Document Intelligence (reading invoices) + Microsoft Foundry (suggesting the account) | Document Intelligence's prebuilt invoice model is GA and available in France Central; Content Understanding isn't there yet. The account is suggested from the supplier's history first, then by `gpt-5.4-mini` (Data Zone Standard, France Central) with structured outputs. Checked 2026-10-24. See [ADR 0004](adr/0004-read-invoices-with-document-intelligence.md) and [ADR 0005](adr/0005-suggest-ledger-accounts-with-rules-then-a-language-model.md) |
| Payments | Stripe | Subscription plans |

## Testing

- xUnit for unit tests.
- Testcontainers for integration tests against a real SQL Server in Docker.

## Roadmap

- **v0.1 – Foundation**: Clean Architecture solution, API + EF Core + SQL Server, Angular, Docker, Aspire locally, tests.
- **v0.2 – Pipeline**: GitHub Actions (build + tests + image), Bicep, deploy to Container Apps + Azure SQL.
- **v0.3 – Product**: auth, multi-tenancy, core feature.
- **v0.4 – Production-ready**: Managed Identity everywhere, Application Insights. Key Vault and HybridCache → Redis postponed until they solve a real problem (2026-10-26).
- **v0.5 – AI**: the AI feature that adds real value to the product.
- **v0.6 – Monetization**: Stripe, plans.
- **v1.x (optional)**: Kubernetes, first locally (k3d/kind + Helm), then AKS if worth it.
