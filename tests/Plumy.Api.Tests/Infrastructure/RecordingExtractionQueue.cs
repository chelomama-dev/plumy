using System.Collections.Concurrent;
using Plumy.Application.Invoices.Extraction;

namespace Plumy.Api.Tests.Infrastructure;

/// <summary>Stands in for Service Bus in the API tests. Shared by the whole collection, so tests filter by id.</summary>
public sealed class RecordingExtractionQueue : IInvoiceExtractionQueue
{
    private readonly ConcurrentQueue<InvoiceExtractionRequest> _requests = new();

    public IReadOnlyCollection<InvoiceExtractionRequest> Requests => _requests;

    public IEnumerable<InvoiceExtractionRequest> For(Guid invoiceId) => _requests.Where(r => r.InvoiceId == invoiceId);

    public Task EnqueueAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(request);
        return Task.CompletedTask;
    }
}
