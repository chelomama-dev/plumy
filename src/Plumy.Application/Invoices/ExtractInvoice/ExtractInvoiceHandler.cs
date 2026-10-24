using Microsoft.Extensions.Logging;
using Plumy.Application.Abstractions;
using Plumy.Application.Invoices.Extraction;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.ExtractInvoice;

/// <summary>
/// Reads one invoice's data. Runs in the worker, once per message. Messages arrive at least once, so running it
/// twice for the same invoice must be harmless: a finished invoice is left as it is.
/// </summary>
public sealed partial class ExtractInvoiceHandler(
    IInvoiceRepository invoices,
    IFileStorage storage,
    IInvoiceExtractor extractor,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ExtractInvoiceHandler> logger)
{
    public const string MissingFileError = "The invoice's file could not be found.";
    public const string GaveUpError = "The invoice could not be read after several attempts. Try again later.";

    /// <summary>
    /// Throws when the error may be transient and this isn't the last attempt: the caller hands the message back to
    /// the queue for a retry.
    /// </summary>
    public async Task<ExtractInvoiceOutcome> HandleAsync(
        ExtractInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
            return ExtractInvoiceOutcome.NotFound;

        if (invoice.ExtractionStatus is InvoiceExtractionStatus.Extracted or InvoiceExtractionStatus.Failed)
            return ExtractInvoiceOutcome.AlreadyFinished;

        // Saved straight away, so the list shows it as in progress while the (slow) read runs
        invoice.StartExtraction();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ExtractedInvoiceData data;
        try
        {
            data = await ReadAsync(invoice, cancellationToken);
        }
        catch (InvoiceExtractionException ex)
        {
            LogUnreadable(ex, invoice.Id);
            return await FailAsync(invoice, ex.Message, cancellationToken);
        }
        catch (Exception ex) when (command.IsLastAttempt && ex is not OperationCanceledException)
        {
            LogGaveUp(ex, invoice.Id);
            return await FailAsync(invoice, GaveUpError, cancellationToken);
        }

        invoice.CompleteExtraction(data, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExtractInvoiceOutcome.Extracted;
    }

    private async Task<ExtractedInvoiceData> ReadAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        await using var content = await storage.OpenReadAsync(invoice.StoragePath, cancellationToken)
            ?? throw new InvoiceExtractionException(MissingFileError);

        return await extractor.ExtractAsync(content, invoice.Format, cancellationToken);
    }

    private async Task<ExtractInvoiceOutcome> FailAsync(
        Invoice invoice, string reason, CancellationToken cancellationToken)
    {
        invoice.FailExtraction(reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExtractInvoiceOutcome.Failed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invoice {InvoiceId} can't be read")]
    private partial void LogUnreadable(Exception exception, Guid invoiceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invoice {InvoiceId} failed on its last attempt")]
    private partial void LogGaveUp(Exception exception, Guid invoiceId);
}
