using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plumy.Application.Abstractions;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Firms;
using Plumy.Infrastructure.Persistence;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseName = "plumydb";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        // AddSqlServerDbContext would pool the context, and a pooled context can't take scoped services like the
        // current firm. So: a plain registration, then Aspire adds retries, health check and telemetry on top
        var connectionString = builder.Configuration.GetConnectionString(DatabaseName)
            ?? throw new InvalidOperationException($"Missing the '{DatabaseName}' connection string.");
        builder.Services.AddDbContext<PlumyDbContext>(options => options.UseSqlServer(connectionString));
        builder.EnrichSqlServerDbContext<PlumyDbContext>();

        // Same scoped DbContext instance, so repositories and the unit of work share one change tracker
        builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PlumyDbContext>());
        builder.Services.AddScoped<IClientCompanyRepository, ClientCompanyRepository>();
        builder.Services.AddScoped<IFirmRepository, FirmRepository>();

        return builder;
    }
}
