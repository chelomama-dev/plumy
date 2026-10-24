using Microsoft.Extensions.DependencyInjection.Extensions;
using Plumy.Application.Abstractions;
using Plumy.Application.Invoices.ExtractInvoice;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Infrastructure;
using Plumy.Worker;

// Reads invoices in the background and suggests their ledger account: takes the messages the API puts on the
// Service Bus queue (see ADR 0003 and 0005)
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.AddInvoiceExtractor();
builder.AddLedgerAccountClassifier();
builder.AddAzureServiceBusClient(DependencyInjection.MessagingName);

// Only the handler the worker runs: AddApplication would also bring the API's handlers, which need a signed-in
// user, and the host's validation in Development would (rightly) refuse to start without one
builder.Services.AddScoped<ExtractInvoiceHandler>();
builder.Services.AddScoped<LedgerAccountSuggester>();
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<MessageTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MessageTenantContext>());

builder.Services.AddOptions<InvoiceExtractionOptions>()
    .BindConfiguration(InvoiceExtractionOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<InvoiceExtractionMessageHandler>();
builder.Services.AddHostedService<InvoiceExtractionWorker>();

await builder.Build().RunAsync();
