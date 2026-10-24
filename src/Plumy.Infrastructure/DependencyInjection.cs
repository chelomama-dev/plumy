using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plumy.Application.Abstractions;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Firms;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.Extraction;
using Plumy.Infrastructure.Extraction;
using Plumy.Infrastructure.Messaging;
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

    /// <summary>
    /// Connection name of Service Bus. Locally (Aspire, the emulator) a connection string; in Azure, the namespace's
    /// host name, which makes the client sign in with the managed identity.
    /// </summary>
    public const string MessagingName = "messaging";

    public const string ExtractionSection = "InvoiceExtraction";

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

    /// <summary>
    /// The sending side, for the API. With InvoiceExtraction:Enabled = false nothing is sent and Service Bus isn't
    /// needed at all: the switch to deploy the pipeline before the real reader is ready.
    /// </summary>
    public static IHostApplicationBuilder AddInvoiceExtractionQueue(this IHostApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue($"{ExtractionSection}:Enabled", defaultValue: true))
        {
            builder.Services.AddSingleton<IInvoiceExtractionQueue, DisabledInvoiceExtractionQueue>();
            return builder;
        }

        builder.AddAzureServiceBusClient(MessagingName);
        builder.Services.AddSingleton<IInvoiceExtractionQueue, ServiceBusInvoiceExtractionQueue>();
        return builder;
    }

    /// <summary>
    /// The reader, for the worker, chosen with InvoiceExtraction:Extractor. Only "Fake" for now; failing at startup
    /// on anything else beats silently using fake data.
    /// </summary>
    public static IHostApplicationBuilder AddInvoiceExtractor(this IHostApplicationBuilder builder)
    {
        var extractor = builder.Configuration[$"{ExtractionSection}:Extractor"];
        switch (extractor)
        {
            case "Fake":
                builder.Services.AddSingleton<IInvoiceExtractor, FakeInvoiceExtractor>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown {ExtractionSection}:Extractor '{extractor}'. Supported: Fake.");
        }

        return builder;
    }

    /// <summary>Creates the blob container. Development only: in Azure it is declared in Bicep.</summary>
    public static Task CreateStorageContainersAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<BlobFileStorage>().CreateContainerIfNotExistsAsync(cancellationToken);
}
