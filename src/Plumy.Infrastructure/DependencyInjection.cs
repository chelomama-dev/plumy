using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plumy.Application.Abstractions;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Firms;
using Plumy.Application.Invoices;
using Plumy.Infrastructure.Persistence;
using Plumy.Infrastructure.Persistence.Repositories;
using Plumy.Infrastructure.Storage;

namespace Plumy.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseName = "plumydb";

    /// <summary>
    /// Connection name of Blob Storage. Locally (Aspire, Azurite) it is a connection string; in Azure, the account's
    /// blob endpoint URL, which makes the client sign in with the managed identity instead of using keys.
    /// </summary>
    public const string BlobsName = "blobs";

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
        builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();

        // Singleton client with retries, health check and telemetry from Aspire
        builder.AddAzureBlobServiceClient(BlobsName);
        builder.Services.AddSingleton<BlobFileStorage>();
        builder.Services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<BlobFileStorage>());

        return builder;
    }

    /// <summary>Creates the blob container. Development only: in Azure it is declared in Bicep.</summary>
    public static Task CreateStorageContainersAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<BlobFileStorage>().CreateContainerIfNotExistsAsync(cancellationToken);
}
