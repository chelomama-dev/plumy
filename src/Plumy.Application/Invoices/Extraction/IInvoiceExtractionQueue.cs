using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.Extraction;

/// <summary>
/// Hands an invoice to the background worker that reads its data (Azure Service Bus). Delivery is at least once:
/// the worker must cope with the same request arriving twice.
/// </summary>
public interface IInvoiceExtractionQueue
{
    Task EnqueueAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The message itself. Ids only: the worker reads the rest from the database, so a message can never carry stale
/// data or anything personal. The firm travels with it because the worker has no user to look it up from.
/// </summary>
/// <param name="AttemptId">
/// The invoice's <c>ExtractionAttemptId</c> when it was queued: a message whose attempt is no longer the invoice's
/// is dropped. Null only in messages sent before it existed, which are processed as before.
/// </param>
public sealed record InvoiceExtractionRequest(Guid InvoiceId, Guid FirmId, Guid? AttemptId)
{
    public static InvoiceExtractionRequest For(Invoice invoice) =>
        new(invoice.Id, invoice.FirmId, invoice.ExtractionAttemptId);
}
