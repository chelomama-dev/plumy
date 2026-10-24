using Microsoft.Extensions.Logging;
using Plumy.Application.Abstractions;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.ExtractInvoice;

/// <summary>
/// Reads one invoice's data, then suggests its ledger account. Runs in the worker, once per message. Messages arrive
/// at least once, so running it twice for the same invoice must be harmless: whatever is already done is skipped.
/// </summary>
public sealed partial class ExtractInvoiceHandler(
    IInvoiceRepository invoices,
    IFileStorage storage,
    IInvoiceExtractor extractor,
    LedgerAccountSuggester suggester,
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

        switch (invoice.ExtractionStatus)
        {
            case InvoiceExtractionStatus.Failed:
                return ExtractInvoiceOutcome.AlreadyFinished;
            case InvoiceExtractionStatus.Extracted when !invoice.NeedsLedgerAccountSuggestion:
                return ExtractInvoiceOutcome.AlreadyFinished;
            case InvoiceExtractionStatus.Pending or InvoiceExtractionStatus.Processing:
                if (await ExtractAsync(invoice, command, cancellationToken) is { } failed)
                    return failed;
                break;
        }

        // Read, by this delivery or an earlier one whose suggestion failed: only the suggestion is left
        await SuggestLedgerAccountAsync(invoice, command, cancellationToken);
        return ExtractInvoiceOutcome.Extracted;
    }

    /// <returns>Failed when the invoice couldn't be read; null when it was.</returns>
    private async Task<ExtractInvoiceOutcome?> ExtractAsync(
        Invoice invoice, ExtractInvoiceCommand command, CancellationToken cancellationToken)
    {
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

        // Checked as it is stored: the warnings are part of what was read. Saved before the suggestion: if that
        // fails, the retry finds the data already read and doesn't pay for reading it again
        var validation = await ValidateAsync(invoice, data, cancellationToken);
        invoice.CompleteExtraction(data, validation, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<InvoiceValidation> ValidateAsync(
        Invoice invoice, ExtractedInvoiceData data, CancellationToken cancellationToken)
    {
        // The foreign key guarantees the company; archived companies keep their invoices
        var companyTaxId = await invoices.GetClientCompanyTaxIdAsync(invoice.ClientCompanyId, cancellationToken)
            ?? throw new InvalidOperationException($"The company of invoice {invoice.Id} doesn't exist.");

        var duplicateOf = data is { SupplierTaxId: { } supplierTaxId, InvoiceNumber: { } number }
            ? await invoices.FindDuplicateAsync(invoice.Id, invoice.ClientCompanyId, supplierTaxId, number,
                cancellationToken)
            : null;

        return InvoiceValidator.Validate(
            data, companyTaxId, duplicateOf, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }

    // A transient error throws, so the message is retried. On the last attempt the invoice is left without a
    // suggestion: it is read, and a person can still pick the account
    private async Task SuggestLedgerAccountAsync(
        Invoice invoice, ExtractInvoiceCommand command, CancellationToken cancellationToken)
    {
        LedgerAccountSuggestion? suggestion;
        try
        {
            suggestion = await suggester.SuggestAsync(invoice, cancellationToken);
        }
        catch (Exception ex) when (command.IsLastAttempt && ex is not OperationCanceledException)
        {
            LogSuggestionGaveUp(ex, invoice.Id);
            return;
        }

        if (suggestion is null)
        {
            LogNoSuggestion(invoice.Id);
            return;
        }

        invoice.SuggestLedgerAccount(suggestion);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No ledger account could be suggested for invoice {InvoiceId} on its last attempt")]
    private partial void LogSuggestionGaveUp(Exception exception, Guid invoiceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "No ledger account to suggest for invoice {InvoiceId}")]
    private partial void LogNoSuggestion(Guid invoiceId);
}
