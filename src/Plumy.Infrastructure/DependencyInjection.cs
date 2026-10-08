using Microsoft.Extensions.Hosting;
using Plumy.Infrastructure.Persistence;

namespace Plumy.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseName = "plumydb";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        // Reads ConnectionStrings:plumydb (injected by the AppHost) and adds retries, health check and telemetry
        builder.AddSqlServerDbContext<PlumyDbContext>(DatabaseName);

        return builder;
    }
}
