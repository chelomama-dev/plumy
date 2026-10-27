using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Application.Invoices.Extraction;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.RetryExtraction;

/// <summary>
/// Queues an invoice's extraction again: after it failed, or when it is stuck in Pending because queueing it
/// failed right after the upload.
/// </summary>
public sealed class RetryInvoiceExtractionHandler(
    IInvoiceRepository invoices,
    IInvoiceExtractionQueue queue,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <returns>The invoice as it is now, or null if it doesn't exist (or is another firm's).</returns>
    public async Task<InvoiceResponse?> HandleAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByIdAsync(invoiceId, cancellationToken);
        if (invoice is null)
            return null;

        var now = clock.GetUtcNow();

        // Expected when two people click at once, or the list is out of date: a conflict, not a bad request
        switch (invoice.ExtractionStatus)
        {
            case InvoiceExtractionStatus.Pending when !invoice.IsStuckInQueue(now):
                throw new ConflictException("The invoice was queued moments ago and is about to be read.");
            case InvoiceExtractionStatus.Processing:
                throw new ConflictException("The invoice is already being read.");
            case InvoiceExtractionStatus.Extracted:
                throw new ConflictException("The invoice has already been read.");
        }

        invoice.RetryExtraction(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Unlike the upload, a failure here reaches the user: they asked for exactly this, and can click again
        await queue.EnqueueAsync(InvoiceExtractionRequest.For(invoice), cancellationToken);

        return await invoices.GetResponseByIdAsync(invoice.Id, cancellationToken);
    }
}
