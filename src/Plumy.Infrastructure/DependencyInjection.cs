using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plumy.Application.Abstractions;
using Plumy.Application.ClientCompanies;
using Plumy.Infrastructure.Persistence;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseName = "plumydb";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        // Reads ConnectionStrings:plumydb (injected by the AppHost) and adds retries, health check and telemetry
        builder.AddSqlServerDbContext<PlumyDbContext>(DatabaseName);

        // Same scoped DbContext instance, so repositories and the unit of work share one change tracker
        builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PlumyDbContext>());
        builder.Services.AddScoped<IClientCompanyRepository, ClientCompanyRepository>();

        return builder;
    }
}
