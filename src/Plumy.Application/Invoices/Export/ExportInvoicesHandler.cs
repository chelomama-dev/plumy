using Plumy.Domain.Accounting;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.Export;

/// <summary>
/// The approved invoices of a company (or of the whole firm) in a period, as a file to import into the accounting
/// software or work with in Excel. Reading only: exporting doesn't change the invoices, so it can be repeated.
/// </summary>
public sealed class ExportInvoicesHandler(IInvoiceRepository invoices, IEnumerable<IInvoiceExportWriter> writers)
{
    /// <summary>
    /// The file is built in memory: a ceiling keeps one request from taking the API down. A firm's month is far
    /// below it; a bigger export asks for a shorter period.
    /// </summary>
    public const int MaxInvoices = 10_000;

    /// <returns>Null when the company doesn't exist (or is another firm's).</returns>
    public async Task<ExportedFile?> HandleAsync(ExportInvoicesQuery query, CancellationToken cancellationToken = default)
    {
        if (query is { From: { } from, To: { } to } && from > to)
            throw new DomainException("The start date must not be after the end date.");

        var writer = writers.SingleOrDefault(w => w.Format == query.Format)
            ?? throw new InvalidOperationException($"No writer for {query.Format}.");

        string? companyTaxId = null;
        if (query.ClientCompanyId is { } companyId)
        {
            // Archived companies too: their invoices are still part of the books
            var taxId = await invoices.GetClientCompanyTaxIdAsync(companyId, cancellationToken);
            if (taxId is null)
                return null;
            companyTaxId = taxId.Value;
        }

        // One over the limit, to know it was exceeded without counting everything
        var sources = await invoices.ListForExportAsync(
            query.ClientCompanyId, query.From, query.To, MaxInvoices + 1, cancellationToken);
        if (sources.Count > MaxInvoices)
            throw new DomainException(
                $"More than {MaxInvoices} invoices in that period. Export a shorter period or one company at a time.");

        var rows = sources.Select(ToRow).ToList();

        using var output = new MemoryStream();
        await writer.WriteAsync(rows, output, cancellationToken);

        return new ExportedFile(output.ToArray(), writer.ContentType, FileName(query, companyTaxId, writer.Extension));
    }

    private static InvoiceExportRow ToRow(InvoiceExportSource source)
    {
        var invoice = source.Invoice;
        var data = invoice.ExtractedData;
        // Approved, so a person confirmed it: never a mere suggestion
        var code = invoice.LedgerAccountCode;

        return new InvoiceExportRow(
            source.CompanyName, source.CompanyTaxId, data?.IssueDate, data?.InvoiceNumber, data?.SupplierName,
            data?.SupplierTaxId, data?.NetAmount, data?.TaxAmount, data?.TotalAmount, data?.Currency,
            code, ChartOfAccounts.Find(code)?.Name ?? code, invoice.Issues, invoice.FileName, invoice.Id);
    }

    // ASCII only (a NIF and dates), so it is the same in every browser: facturas-B12345674_2026-10-01_2026-10-31.xlsx
    public static string FileName(ExportInvoicesQuery query, string? companyTaxId, string extension)
    {
        var period = (query.From, query.To) switch
        {
            ({ } from, { } to) => $"_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}",
            ({ } from, null) => $"_desde-{from:yyyy-MM-dd}",
            (null, { } to) => $"_hasta-{to:yyyy-MM-dd}",
            _ => ""
        };

        return $"facturas-{companyTaxId ?? "todas"}{period}{extension}";
    }
}
