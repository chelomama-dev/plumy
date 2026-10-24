namespace Plumy.Domain.Invoices;

/// <summary>
/// Something in a read invoice a person should look at before booking it. Warnings, not errors: the reader can
/// misread, and the invoice may still be right. Stored by name, so the order here can change.
/// </summary>
public enum InvoiceIssue
{
    /// <summary>The supplier's NIF wasn't found: the invoice can't be booked without it.</summary>
    MissingSupplierTaxId,

    /// <summary>A Spanish NIF/CIF/NIE whose check digit is wrong: misread, or a wrong invoice.</summary>
    InvalidSupplierTaxId,

    /// <summary>The invoice is addressed to another NIF than the company it was uploaded to.</summary>
    CustomerTaxIdMismatch,

    MissingInvoiceNumber,
    MissingIssueDate,

    /// <summary>Dated after today.</summary>
    FutureIssueDate,

    MissingTotal,

    /// <summary>Base + VAT (minus a usual IRPF withholding) doesn't give the total.</summary>
    AmountsMismatch,

    /// <summary>The VAT is more than any Spanish rate allows (21% plus the 5.2% equivalence surcharge).</summary>
    UnusualTaxAmount,

    /// <summary>Another invoice of the same company has the same supplier and number.</summary>
    PossibleDuplicate
}
