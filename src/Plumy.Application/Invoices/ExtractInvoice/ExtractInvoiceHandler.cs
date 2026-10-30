using Microsoft.Extensions.Logging;
using Plumy.Application.Abstractions;
using Plumy.Application.Firms;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.ExtractInvoice;

/// <summary>
/// Reads one invoice's data, suggests its ledger account and, if the firm allows it and there is nothing to look at,
/// approves it. Runs in the worker, once per message. Messages arrive at least once, so running it twice for the same
/// invoice must be harmless: whatever is already done is skipped.
/// </summary>
public sealed partial class ExtractInvoiceHandler(
    IInvoiceRepository invoices,
    IFileStorage storage,
    IInvoiceExtractor extractor,
    InvoiceChecks checks,
    LedgerAccountSuggester suggester,
    IFirmRepository firms,
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

        // A message from an earlier queueing (a retry came after it): two messages for one invoice would read it,
        // and pay for it, twice. Whatever state it is in, the current attempt's message takes care of it
        if (command.AttemptId is { } attemptId && attemptId != invoice.ExtractionAttemptId)
        {
            LogSuperseded(invoice.Id, attemptId);
            return ExtractInvoiceOutcome.Superseded;
        }

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

        InvoiceExtractionResult result;
        try
        {
            result = await ReadAsync(invoice, cancellationToken);
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
        var validation = await checks.ValidateAsync(invoice, result.Data, cancellationToken);
        invoice.CompleteExtraction(result.Data, validation, clock.GetUtcNow(), result.Confidence);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return null;
    }

    // A transient error throws, so the message is retried. On the last attempt the suggestion is marked failed: the
    // invoice is read, and a person can still pick the account
    private async Task SuggestLedgerAccountAsync(
        Invoice invoice, ExtractInvoiceCommand command, CancellationToken cancellationToken)
    {
        LedgerAccountSuggestionResult result;
        try
        {
            result = await suggester.SuggestAsync(invoice, cancellationToken);
        }
        catch (Exception ex) when (command.IsLastAttempt && ex is not OperationCanceledException)
        {
            LogSuggestionGaveUp(ex, invoice.Id);
            invoice.FailLedgerAccountSuggestion();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        if (result.Suggestion is { } suggestion)
        {
            invoice.SuggestLedgerAccount(suggestion);
            await ApproveIfAllowedAsync(invoice, cancellationToken);
        }
        else
        {
            LogNoSuggestion(invoice.Id, result.NoSuggestionReason);
            invoice.DeclineLedgerAccountSuggestion(result.NoSuggestionReason);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // Saved with the suggestion, in one go: a retry never finds it suggested but not yet considered for approval
    private async Task ApproveIfAllowedAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        // Only the supplier's history can be approved unseen: no need to ask for the firm's settings otherwise
        if (invoice.LedgerAccountSuggestion?.Source != LedgerAccountSuggestionSource.SupplierHistory)
            return;

        var policy = await firms.GetAutoApprovalAsync(invoice.FirmId, cancellationToken);
        if (!invoice.CanBeApprovedAutomatically(policy))
            return;

        invoice.ApproveAutomatically(policy, clock.GetUtcNow());
        LogApprovedAutomatically(invoice.Id, policy.MinConfidence);
    }

    private async Task<InvoiceExtractionResult> ReadAsync(Invoice invoice, CancellationToken cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Invoice {InvoiceId} was queued again after attempt {AttemptId}: its message is dropped")]
    private partial void LogSuperseded(Guid invoiceId, Guid attemptId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invoice {InvoiceId} can't be read")]
    private partial void LogUnreadable(Exception exception, Guid invoiceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invoice {InvoiceId} failed on its last attempt")]
    private partial void LogGaveUp(Exception exception, Guid invoiceId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No ledger account could be suggested for invoice {InvoiceId} on its last attempt")]
    private partial void LogSuggestionGaveUp(Exception exception, Guid invoiceId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Invoice {InvoiceId} approved automatically (every booked field read with at least {MinConfidence})")]
    private partial void LogApprovedAutomatically(Guid invoiceId, double minConfidence);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No ledger account to suggest for invoice {InvoiceId}: {Reason}")]
    private partial void LogNoSuggestion(Guid invoiceId, string? reason);
}
