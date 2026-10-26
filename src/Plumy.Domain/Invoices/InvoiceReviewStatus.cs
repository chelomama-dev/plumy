namespace Plumy.Domain.Invoices;

/// <summary>
/// What a person decided about the invoice. Apart from <see cref="InvoiceExtractionStatus"/>, which is the machine's
/// work: an invoice can be read and still waiting for someone, or unreadable and rejected.
/// </summary>
public enum InvoiceReviewStatus
{
    /// <summary>Nobody has decided yet. Its data and account can still change.</summary>
    Unreviewed,

    /// <summary>A person checked its data and account: it goes to the books (the export). Locked until reopened.</summary>
    Approved,

    /// <summary>Not to be booked: not an invoice, a copy, the wrong company... The reason says which.</summary>
    Rejected
}
