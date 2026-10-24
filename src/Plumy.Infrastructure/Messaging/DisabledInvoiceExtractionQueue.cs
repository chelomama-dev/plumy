using Microsoft.Extensions.Logging;
using Plumy.Application.Invoices.Extraction;

namespace Plumy.Infrastructure.Messaging;

/// <summary>
/// Used while extraction is switched off (InvoiceExtraction:Enabled = false): invoices stay Pending and can be
/// queued with a retry once it is switched on.
/// </summary>
internal sealed partial class DisabledInvoiceExtractionQueue(ILogger<DisabledInvoiceExtractionQueue> logger)
    : IInvoiceExtractionQueue
{
    public Task EnqueueAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken = default)
    {
        LogSkipped(request.InvoiceId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Extraction is disabled: invoice {InvoiceId} stays Pending")]
    private partial void LogSkipped(Guid invoiceId);
}
