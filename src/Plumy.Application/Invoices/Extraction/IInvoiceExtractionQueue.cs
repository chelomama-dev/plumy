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
public sealed record InvoiceExtractionRequest(Guid InvoiceId, Guid FirmId);
