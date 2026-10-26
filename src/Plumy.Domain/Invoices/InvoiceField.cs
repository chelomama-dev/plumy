namespace Plumy.Domain.Invoices;

/// <summary>
/// The fields of <see cref="ExtractedInvoiceData"/>, by name: what the reader's confidence and a person's corrections
/// refer to. Stored by name, so the order here can change.
/// </summary>
public enum InvoiceField
{
    SupplierName,
    SupplierTaxId,
    CustomerTaxId,
    InvoiceNumber,
    IssueDate,
    NetAmount,
    TaxAmount,
    TotalAmount,
    Currency,
    ItemsDescription
}
