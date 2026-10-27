namespace Plumy.Application.Invoices.ExtractInvoice;

/// <param name="InvoiceId">The invoice to read. The firm is already set in the tenant context.</param>
/// <param name="AttemptId">
/// Which queueing of the invoice the message belongs to. Null (a message older than attempts) is always processed.
/// </param>
/// <param name="IsLastAttempt">
/// The queue won't deliver this message again. A transient error then marks the invoice as failed instead of
/// leaving it to a retry that won't come.
/// </param>
public sealed record ExtractInvoiceCommand(Guid InvoiceId, Guid? AttemptId, bool IsLastAttempt);

public enum ExtractInvoiceOutcome
{
    Extracted,

    /// <summary>Marked as failed: the message is done with, there is nothing left to retry.</summary>
    Failed,

    /// <summary>Already extracted (and its account suggested) or failed by an earlier delivery of the message.</summary>
    AlreadyFinished,

    /// <summary>No such invoice in the message's firm: the message is wrong, not the invoice.</summary>
    NotFound,

    /// <summary>
    /// The invoice was queued again after this message was sent: the newer message does the work, this one is
    /// dropped so the invoice isn't read twice in parallel.
    /// </summary>
    Superseded
}
