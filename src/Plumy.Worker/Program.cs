using Plumy.Application;
using Plumy.Application.Abstractions;
using Plumy.Infrastructure;
using Plumy.Worker;

// Reads invoices in the background and suggests their ledger account: takes the messages the API puts on the
// Service Bus queue (see ADR 0003 and 0005)
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.AddInvoiceExtractor();
builder.AddLedgerAccountClassifier();
builder.AddAzureServiceBusClient(Plumy.Infrastructure.DependencyInjection.MessagingName);

// Only what the worker runs: AddApplication would also bring the API's handlers, which need a signed-in user
builder.Services.AddInvoiceExtraction();

builder.Services.AddScoped<MessageTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MessageTenantContext>());

builder.Services.AddOptions<InvoiceExtractionOptions>()
    .BindConfiguration(InvoiceExtractionOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<InvoiceExtractionMessageHandler>();
builder.Services.AddHostedService<InvoiceExtractionWorker>();

// Checked at startup in every environment, not only Development: a missing registration stops the new revision
// from starting instead of failing every message in production
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true
}));

await builder.Build().RunAsync();
