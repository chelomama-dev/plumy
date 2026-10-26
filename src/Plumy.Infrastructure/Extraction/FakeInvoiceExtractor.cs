using Plumy.Application.Invoices.Extraction;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Extraction;

/// <summary>
/// Pretends to read the invoice: always the same sample data, after a short pause so the list shows it in
/// progress. For local development and tests: no Azure account, nothing paid. The invoice number comes with a low
/// confidence, so the review screen has something to point out.
/// </summary>
internal sealed class FakeInvoiceExtractor(TimeProvider clock) : IInvoiceExtractor
{
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    internal const double LowConfidence = 0.42;
    internal const double HighConfidence = 0.97;

    public async Task<InvoiceExtractionResult> ExtractAsync(
        Stream content, InvoiceFileFormat format, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Delay, clock, cancellationToken);

        var data = ExtractedInvoiceData.Create(
            supplierName: "Proveedor de ejemplo S.L.",
            supplierTaxId: "B12345674",
            invoiceNumber: "EJ-0001",
            issueDate: DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            netAmount: 100m,
            taxAmount: 21m,
            totalAmount: 121m,
            currency: "EUR");

        var confidence = new Dictionary<InvoiceField, double>
        {
            [InvoiceField.SupplierName] = HighConfidence,
            [InvoiceField.SupplierTaxId] = HighConfidence,
            [InvoiceField.InvoiceNumber] = LowConfidence,
            [InvoiceField.IssueDate] = HighConfidence,
            [InvoiceField.NetAmount] = HighConfidence,
            [InvoiceField.TaxAmount] = HighConfidence,
            [InvoiceField.TotalAmount] = HighConfidence,
            [InvoiceField.Currency] = HighConfidence
        };

        return new InvoiceExtractionResult(data, confidence);
    }
}
