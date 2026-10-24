using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Plumy.Application.Invoices.ExtractInvoice;
using Plumy.Infrastructure.Messaging;

namespace Plumy.Worker;

/// <summary>
/// Decides what happens to each message: run the extraction in a scope of its own, then complete, dead-letter or
/// leave it for a retry. Kept apart from the processor so it can be tested without Service Bus.
/// </summary>
internal sealed partial class InvoiceExtractionMessageHandler(
    IServiceScopeFactory scopeFactory,
    IOptions<InvoiceExtractionOptions> options,
    ILogger<InvoiceExtractionMessageHandler> logger)
{
    public const string MalformedReason = "MalformedMessage";
    public const string NotFoundReason = "InvoiceNotFound";

    public async Task HandleAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        // Retrying won't fix a broken body: straight to the dead-letter queue, where a person can look at it
        if (InvoiceExtractionMessages.FromMessage(message) is not { } request)
        {
            LogMalformed(message.MessageId);
            await args.DeadLetterMessageAsync(message, MalformedReason,
                "The body is not a valid invoice extraction request.", args.CancellationToken);
            return;
        }

        // A scope per message: its own DbContext (not thread-safe, and messages run in parallel) and its own firm
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MessageTenantContext>().Set(request.FirmId);
        var handler = scope.ServiceProvider.GetRequiredService<ExtractInvoiceHandler>();

        // DeliveryCount starts at 1. After this one Service Bus dead-letters the message by itself, so this is the
        // last chance to tell the user the invoice failed
        var isLastAttempt = message.DeliveryCount >= options.Value.MaxDeliveryCount;

        ExtractInvoiceOutcome outcome;
        try
        {
            outcome = await handler.HandleAsync(
                new ExtractInvoiceCommand(request.InvoiceId, isLastAttempt), args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            // Shutting down (a new revision, scale-in): give it back now so another replica takes it straight away
            await args.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            // Transient: the message is left as it is. Its lock expires (LockDuration) and Service Bus delivers it
            // again, which spaces the retries out. Abandoning would redeliver at once and burn every attempt in
            // seconds against a service that is down
            LogWillRetry(ex, request.InvoiceId, message.DeliveryCount);
            return;
        }

        if (outcome == ExtractInvoiceOutcome.NotFound)
        {
            // Deleted, or the firm in the message is wrong: either way, nothing to retry
            LogNotFound(request.InvoiceId, request.FirmId);
            await args.DeadLetterMessageAsync(message, NotFoundReason,
                "No invoice with this id in the message's firm.", args.CancellationToken);
            return;
        }

        LogFinished(request.InvoiceId, outcome);
        await args.CompleteMessageAsync(message, args.CancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} is not a valid extraction request")]
    private partial void LogMalformed(string messageId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Extraction of invoice {InvoiceId} failed on delivery {DeliveryCount}; it will be retried")]
    private partial void LogWillRetry(Exception exception, Guid invoiceId, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invoice {InvoiceId} not found in firm {FirmId}")]
    private partial void LogNotFound(Guid invoiceId, Guid firmId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invoice {InvoiceId}: {Outcome}")]
    private partial void LogFinished(Guid invoiceId, ExtractInvoiceOutcome outcome);
}
