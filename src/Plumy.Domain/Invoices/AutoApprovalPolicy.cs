using Plumy.Domain.Common;

namespace Plumy.Domain.Invoices;

/// <summary>
/// When a firm lets Plumy approve an invoice with nobody looking at it: off unless the firm turns it on, and then only
/// for invoices with nothing to look at (see <see cref="Invoice.CanBeApprovedAutomatically"/>).
/// </summary>
public sealed record AutoApprovalPolicy
{
    /// <summary>Below this the reader is wrong too often to book anything unseen (the review screen's doubtful line).</summary>
    public const double LowestMinConfidence = 0.8;

    /// <summary>The top confidence band of the accuracy report: the strictest line that still lets anything through.</summary>
    public const double DefaultMinConfidence = 0.95;

    /// <summary>
    /// The fields that go into the books: each one must have been read with at least <see cref="MinConfidence"/>.
    /// The supplier's name, the currency and the description are not booked, so a doubt about them doesn't block it.
    /// </summary>
    public static readonly IReadOnlyList<InvoiceField> BookedFields =
    [
        InvoiceField.SupplierTaxId,
        InvoiceField.InvoiceNumber,
        InvoiceField.IssueDate,
        InvoiceField.NetAmount,
        InvoiceField.TaxAmount,
        InvoiceField.TotalAmount
    ];

    public static readonly AutoApprovalPolicy Disabled = new(false, DefaultMinConfidence);

    public bool Enabled { get; private init; }

    /// <summary>From <see cref="LowestMinConfidence"/> to 1. Kept while disabled, so turning it on again restores it.</summary>
    public double MinConfidence { get; private init; }

    private AutoApprovalPolicy(bool enabled, double minConfidence)
    {
        Enabled = enabled;
        MinConfidence = minConfidence;
    }

    public static AutoApprovalPolicy Create(bool enabled, double minConfidence)
    {
        if (!(minConfidence is >= LowestMinConfidence and <= 1))
            throw new DomainException(
                $"The minimum confidence must be between {LowestMinConfidence:0.##} and 1.");

        return new AutoApprovalPolicy(enabled, minConfidence);
    }
}
