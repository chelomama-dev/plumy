# Tech stack & roadmap

Status: accepted (2026-10-04). The product idea is still to be defined.

## Core

| Area | Choice | Notes |
|---|---|---|
| Backend | .NET 10 (C#), ASP.NET Core Web API | EF Core as ORM |
| Frontend | Angular | |
| Database | SQL Server (Azure SQL Database in the cloud) | |
| Architecture | Clean Architecture (light) | 4 projects: Domain, Application, Infrastructure, Api. No MediatR (commercial license since 2025): own handlers |
| Local orchestration | Aspire 13 | API + SQL + Redis + Angular with one command, built-in telemetry dashboard |
| Containers | Docker | From day 1 |

## Cloud & DevOps

| Area | Choice | Notes |
|---|---|---|
| Hosting | Azure Container Apps | Kubernetes-based, scale to zero, free monthly grant. AKS is not used initially (always-on node cost) |
| CI/CD | GitHub Actions | Build, tests, Docker image, deploy |
| IaC | Bicep | No resources created manually in the portal |
| Secrets | Managed Identity + Azure Key Vault | No passwords in `appsettings` |
| Observability | OpenTelemetry + Application Insights | |

## Product features

| Area | Choice | Notes |
|---|---|---|
| Auth | ASP.NET Identity or Entra External ID | To decide in v0.3. Multi-tenant |
| Caching | HybridCache → Redis as L2 | Redis container first; Azure Managed Redis only if needed |
| AI | Azure OpenAI / Microsoft Foundry | Aligned with the AI-200 certification |
| Payments | Stripe | Subscription plans |

## Testing

- xUnit for unit tests.
- Testcontainers for integration tests against a real SQL Server in Docker.

## Roadmap

- **v0.1 – Foundation**: Clean Architecture solution, API + EF Core + SQL Server, Angular, Docker, Aspire locally, tests.
- **v0.2 – Pipeline**: GitHub Actions (build + tests + image), Bicep, deploy to Container Apps + Azure SQL.
- **v0.3 – Product**: auth, multi-tenancy, core feature.
- **v0.4 – Production-ready**: Key Vault, Managed Identity, Application Insights, HybridCache → Redis.
- **v0.5 – AI**: the AI feature that adds real value to the product.
- **v0.6 – Monetization**: Stripe, plans.
- **v1.x (optional)**: Kubernetes, first locally (k3d/kind + Helm), then AKS if worth it.
