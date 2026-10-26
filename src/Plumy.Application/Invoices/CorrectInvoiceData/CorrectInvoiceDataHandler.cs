using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.CorrectInvoiceData;

/// <summary>The whole invoice as the person left it: a field left empty is cleared, not kept.</summary>
public sealed record CorrectInvoiceDataCommand(
    Guid InvoiceId,
    string? SupplierName,
    string? SupplierTaxId,
    string? CustomerTaxId,
    string? InvoiceNumber,
    DateOnly? IssueDate,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    string? Currency,
    string? ItemsDescription);

/// <summary>
/// A person fixes what the reader got wrong. The checks run again on the new values, so a fixed NIF or total stops
/// being flagged (and a new mistake starts being).
/// </summary>
public sealed class CorrectInvoiceDataHandler(
    IInvoiceRepository invoices,
    InvoiceChecks checks,
    IUnitOfWork unitOfWork)
{
    /// <returns>The invoice as it is now, or null if it doesn't exist (or is another firm's).</returns>
    public async Task<InvoiceResponse?> HandleAsync(
        CorrectInvoiceDataCommand command, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
            return null;

        // The screen may be out of date (the worker hasn't finished, someone else approved it): a conflict
        if (invoice.ExtractionStatus != InvoiceExtractionStatus.Extracted)
            throw new ConflictException("The invoice hasn't been read yet.");
        if (invoice.ReviewStatus != InvoiceReviewStatus.Unreviewed)
            throw new ConflictException($"The invoice is {invoice.ReviewStatus}: reopen it to change it.");

        // Tidied like the reader's values (NIF separators, rounding), so both compare and search the same way
        var data = ExtractedInvoiceData.Create(
            supplierName: command.SupplierName,
            supplierTaxId: command.SupplierTaxId,
            customerTaxId: command.CustomerTaxId,
            invoiceNumber: command.InvoiceNumber,
            issueDate: command.IssueDate,
            netAmount: command.NetAmount,
            taxAmount: command.TaxAmount,
            totalAmount: command.TotalAmount,
            currency: command.Currency,
            items: [command.ItemsDescription]);

        // Create drops an amount out of range, which suits a misread; a person's typo must be told instead
        if ((command.NetAmount is not null && data.NetAmount is null)
            || (command.TaxAmount is not null && data.TaxAmount is null)
            || (command.TotalAmount is not null && data.TotalAmount is null))
            throw new DomainException("An amount is out of range.");

        var validation = await checks.ValidateAsync(invoice, data, cancellationToken);
        invoice.CorrectData(data, validation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await invoices.GetResponseByIdAsync(invoice.Id, cancellationToken);
    }
}
