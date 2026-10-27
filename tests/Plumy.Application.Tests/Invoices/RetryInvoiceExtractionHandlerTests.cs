using Plumy.Application.Common;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.RetryExtraction;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class RetryInvoiceExtractionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly RecordingExtractionQueue _queue = new();
    private readonly RetryInvoiceExtractionHandler _handler;

    public RetryInvoiceExtractionHandlerTests() =>
        _handler = new RetryInvoiceExtractionHandler(_invoices, _queue, _invoices, new FixedTimeProvider(Now));

    private async Task<Invoice> AddInvoiceAsync(Action<Invoice>? arrange = null, DateTimeOffset? uploadedAt = null)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, Guid.CreateVersion7(), "factura.pdf", InvoiceFileFormat.Pdf,
            1000, new string('a', 64), Guid.NewGuid(), uploadedAt ?? Now);
        arrange?.Invoke(invoice);
        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    private static void Fail(Invoice invoice)
    {
        invoice.StartExtraction();
        invoice.FailExtraction("Unreadable.");
    }

    [Fact]
    public async Task HandleAsync_FailedInvoice_IsPendingAndQueuedAgain()
    {
        var invoice = await AddInvoiceAsync(Fail);

        var response = await _handler.HandleAsync(invoice.Id);

        Assert.Equal("Pending", response!.ExtractionStatus);
        Assert.Null(response.ExtractionError);
        Assert.Equal(Now, response.ExtractionQueuedAt);
        // A new attempt: the message carries it, so a leftover message of the earlier one is dropped
        Assert.Equal(new InvoiceExtractionRequest(invoice.Id, TestTenant.FirmId, invoice.ExtractionAttemptId),
            Assert.Single(_queue.Requests));
    }

    [Fact]
    public async Task HandleAsync_PendingForLong_IsQueuedAgain()
    {
        var invoice = await AddInvoiceAsync(uploadedAt: Now - Invoice.StuckPendingAfter);

        await _handler.HandleAsync(invoice.Id);

        Assert.Single(_queue.Requests);
    }

    [Fact]
    public async Task HandleAsync_PendingSinceMomentsAgo_IsAConflict()
    {
        // A second click (or a second person) right after a retry: its message is still on the way
        var invoice = await AddInvoiceAsync(uploadedAt: Now.AddMinutes(-1));

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(invoice.Id));
        Assert.Empty(_queue.Requests);
    }

    [Fact]
    public async Task HandleAsync_Twice_TheSecondIsAConflict()
    {
        var invoice = await AddInvoiceAsync(Fail);

        await _handler.HandleAsync(invoice.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(invoice.Id));
        Assert.Single(_queue.Requests);
    }

    [Fact]
    public async Task HandleAsync_UnknownInvoice_ReturnsNull()
    {
        Assert.Null(await _handler.HandleAsync(Guid.NewGuid()));
        Assert.Empty(_queue.Requests);
    }

    [Fact]
    public async Task HandleAsync_ProcessingOrExtracted_IsAConflict()
    {
        var processing = await AddInvoiceAsync(i => i.StartExtraction());
        var extracted = await AddInvoiceAsync(i =>
        {
            i.StartExtraction();
            i.CompleteExtraction(ExtractedInvoiceData.Create(), InvoiceValidation.None, Now);
        });

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(processing.Id));
        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(extracted.Id));
        Assert.Empty(_queue.Requests);
    }

    [Fact]
    public async Task HandleAsync_WhenQueueingFails_Throws()
    {
        // The user asked for exactly this, so they must know it didn't happen
        var invoice = await AddInvoiceAsync(Fail);
        _queue.FailWith = new InvalidOperationException("Service Bus down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(invoice.Id));
    }
}
