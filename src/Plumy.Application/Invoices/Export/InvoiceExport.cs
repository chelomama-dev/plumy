using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.Export;

public enum InvoiceExportFormat
{
    /// <summary>A real spreadsheet: numbers and dates as such, filters on the header.</summary>
    Xlsx,

    /// <summary>Plain text that Excel in Spanish opens as it is (semicolons, decimal comma, UTF-8 with BOM).</summary>
    Csv
}

/// <param name="ClientCompanyId">One company, or null for every company of the firm.</param>
/// <param name="From">Issue date from, inclusive. An invoice with no date is left out when there is a range.</param>
/// <param name="To">Issue date to, inclusive.</param>
public sealed record ExportInvoicesQuery(
    Guid? ClientCompanyId, DateOnly? From, DateOnly? To, InvoiceExportFormat Format);

/// <summary>An approved invoice, with its company, as the repository loads it for the export.</summary>
public sealed record InvoiceExportSource(Invoice Invoice, string CompanyName, string CompanyTaxId);

/// <summary>One line of the export: what an accountant needs to book the invoice.</summary>
/// <param name="LedgerAccountCode">The account a person confirmed.</param>
public sealed record InvoiceExportRow(
    string CompanyName,
    string CompanyTaxId,
    DateOnly? IssueDate,
    string? InvoiceNumber,
    string? SupplierName,
    string? SupplierTaxId,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    string? Currency,
    string? LedgerAccountCode,
    string? LedgerAccountName,
    IReadOnlyList<InvoiceIssue> Issues,
    string FileName,
    Guid InvoiceId);

/// <summary>Writes the rows in one file format (ClosedXML for Excel, our own writer for CSV).</summary>
public interface IInvoiceExportWriter
{
    InvoiceExportFormat Format { get; }
    string ContentType { get; }

    /// <summary>With the dot, e.g. ".xlsx".</summary>
    string Extension { get; }

    Task WriteAsync(IReadOnlyList<InvoiceExportRow> rows, Stream output, CancellationToken cancellationToken = default);
}

/// <summary>The finished file.</summary>
public sealed record ExportedFile(byte[] Content, string ContentType, string FileName);
