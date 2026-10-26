using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plumy.Application.Abstractions;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.ExtractInvoice;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Application.Tests.Invoices;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Messaging;

namespace Plumy.Worker.Tests;

public class InvoiceExtractionMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirmId = Guid.CreateVersion7();
    private const int MaxDeliveryCount = 5;

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly FakeExtractor _extractor = new();
    private readonly FakeReceiver _receiver = new();
    private readonly InvoiceExtractionMessageHandler _handler;

    /// <summary>The firm the tenant context had while the file was being read.</summary>
    private Guid? _firmDuringRead;

    public InvoiceExtractionMessageHandlerTests()
    {
        // The worker's own registrations, with in-memory stand-ins for SQL, Blob Storage, the reader and the classifier
        var services = new ServiceCollection();
        services.AddSingleton<IInvoiceRepository>(_invoices);
        services.AddSingleton<IUnitOfWork>(_invoices);
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<MessageTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MessageTenantContext>());
        services.AddScoped<IInvoiceExtractor>(sp =>
        {
            var tenant = sp.GetRequiredService<ITenantContext>();
            _extractor.OnExtract = () => _firmDuringRead = tenant.FirmId;
            return _extractor;
        });
        services.AddSingleton<ILedgerAccountClassifier>(new FakeClassifier());
        services.AddScoped<InvoiceChecks>();
        services.AddScoped<LedgerAccountSuggester>();
        services.AddScoped<ExtractInvoiceHandler>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _handler = new InvoiceExtractionMessageHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new InvoiceExtractionOptions { MaxDeliveryCount = MaxDeliveryCount }),
            NullLogger<InvoiceExtractionMessageHandler>.Instance);
    }

    private async Task<Invoice> AddInvoiceAsync()
    {
        var invoice = Invoice.Create(FirmId, Guid.CreateVersion7(), "factura.png", InvoiceFileFormat.Png, 1000,
            new string('a', 64), Guid.NewGuid(), Now);
        await _storage.UploadAsync(invoice.StoragePath, new MemoryStream(TestFiles.Png()), "image/png");
        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    private Task HandleAsync(BinaryData body, int deliveryCount = 1, CancellationToken cancellationToken = default)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: body, messageId: Guid.NewGuid().ToString(), deliveryCount: deliveryCount);
        return _handler.HandleAsync(new ProcessMessageEventArgs(message, _receiver, cancellationToken));
    }

    private Task HandleAsync(Guid invoiceId, int deliveryCount = 1, CancellationToken cancellationToken = default) =>
        HandleAsync(InvoiceExtractionMessages.ToMessage(new InvoiceExtractionRequest(invoiceId, FirmId)).Body,
            deliveryCount, cancellationToken);

    [Fact]
    public async Task ValidMessage_ExtractsInTheMessagesFirmAndCompletes()
    {
        var invoice = await AddInvoiceAsync();

        await HandleAsync(invoice.Id);

        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Equal(FirmId, _firmDuringRead);
        Assert.Equal(["Complete"], _receiver.Settlements);
    }

    [Fact]
    public async Task SameMessageTwice_CompletesBoth_ReadsOnce()
    {
        var invoice = await AddInvoiceAsync();

        await HandleAsync(invoice.Id);
        await HandleAsync(invoice.Id);

        Assert.Single(_extractor.Calls);
        Assert.Equal(["Complete", "Complete"], _receiver.Settlements);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task MalformedMessage_IsDeadLettered(string body)
    {
        await HandleAsync(BinaryData.FromString(body));

        Assert.Equal(["DeadLetter"], _receiver.Settlements);
        Assert.Equal(InvoiceExtractionMessageHandler.MalformedReason, _receiver.DeadLetterReason);
    }

    [Fact]
    public async Task UnknownInvoice_IsDeadLettered()
    {
        await HandleAsync(Guid.NewGuid());

        Assert.Equal(["DeadLetter"], _receiver.Settlements);
        Assert.Equal(InvoiceExtractionMessageHandler.NotFoundReason, _receiver.DeadLetterReason);
    }

    [Fact]
    public async Task UnreadableInvoice_FailsAndCompletes()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new InvoiceExtractionException("The file is not an invoice.");

        await HandleAsync(invoice.Id);

        Assert.Equal(InvoiceExtractionStatus.Failed, invoice.ExtractionStatus);
        Assert.Equal(["Complete"], _receiver.Settlements);
    }

    [Fact]
    public async Task TransientError_LeavesTheMessageForTheLockToExpire()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new HttpRequestException("503");

        await HandleAsync(invoice.Id, deliveryCount: MaxDeliveryCount - 1);

        // Not abandoned: that would redeliver it at once, with no time for the service to recover
        Assert.Empty(_receiver.Settlements);
        Assert.Equal(InvoiceExtractionStatus.Processing, invoice.ExtractionStatus);
    }

    [Fact]
    public async Task TransientErrorOnTheLastDelivery_FailsTheInvoiceAndCompletes()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new HttpRequestException("503");

        await HandleAsync(invoice.Id, deliveryCount: MaxDeliveryCount);

        Assert.Equal(InvoiceExtractionStatus.Failed, invoice.ExtractionStatus);
        Assert.Equal(["Complete"], _receiver.Settlements);
    }

    [Fact]
    public async Task ShuttingDown_AbandonsSoAnotherReplicaTakesIt()
    {
        var invoice = await AddInvoiceAsync();
        // The host is already stopping: the read is cancelled half-way
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        await HandleAsync(invoice.Id, deliveryCount: MaxDeliveryCount, cancellationToken: shutdown.Token);

        Assert.Equal(["Abandon"], _receiver.Settlements);
        Assert.Equal(InvoiceExtractionStatus.Processing, invoice.ExtractionStatus);
    }
}
