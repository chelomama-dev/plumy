using Plumy.Application.Invoices.Extraction;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Extraction;

/// <summary>
/// Pretends to read the invoice: always the same sample data, after a short pause so the list shows it in
/// progress. Only for local development until the real reader (Azure Document Intelligence) is in place.
/// </summary>
internal sealed class FakeInvoiceExtractor(TimeProvider clock) : IInvoiceExtractor
{
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    public async Task<ExtractedInvoiceData> ExtractAsync(
        Stream content, InvoiceFileFormat format, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Delay, clock, cancellationToken);

        return ExtractedInvoiceData.Create(
            supplierName: "Proveedor de ejemplo S.L.",
            supplierTaxId: "B12345674",
            invoiceNumber: "EJ-0001",
            issueDate: DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            netAmount: 100m,
            taxAmount: 21m,
            totalAmount: 121m,
            currency: "EUR");
    }
}
