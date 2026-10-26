using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices;

/// <summary>
/// Runs <see cref="InvoiceValidator"/> on an invoice's data, fetching what the rules need from the database (the
/// company's NIF, an earlier invoice with the same number). The worker uses it on what it read, the API on what a
/// person corrected.
/// </summary>
public sealed class InvoiceChecks(IInvoiceRepository invoices, TimeProvider clock)
{
    public async Task<InvoiceValidation> ValidateAsync(
        Invoice invoice, ExtractedInvoiceData data, CancellationToken cancellationToken = default)
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
}
