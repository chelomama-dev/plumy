namespace Plumy.Application.Invoices.ExtractInvoice;

/// <param name="InvoiceId">The invoice to read. The firm is already set in the tenant context.</param>
/// <param name="IsLastAttempt">
/// The queue won't deliver this message again. A transient error then marks the invoice as failed instead of
/// leaving it to a retry that won't come.
/// </param>
public sealed record ExtractInvoiceCommand(Guid InvoiceId, bool IsLastAttempt);

public enum ExtractInvoiceOutcome
{
    Extracted,

    /// <summary>Marked as failed: the message is done with, there is nothing left to retry.</summary>
    Failed,

    /// <summary>Already extracted or failed by an earlier delivery of the same message.</summary>
    AlreadyFinished,

    /// <summary>No such invoice in the message's firm: the message is wrong, not the invoice.</summary>
    NotFound
}
