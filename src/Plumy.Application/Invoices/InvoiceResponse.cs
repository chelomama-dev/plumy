using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices;

public sealed record InvoiceResponse(
    Guid Id,
    Guid ClientCompanyId,
    string ClientCompanyName,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string ExtractionStatus,
    string? ExtractionError,
    DateTimeOffset? ExtractedAt,
    ExtractedInvoiceDataResponse? ExtractedData)
{
    public static InvoiceResponse From(Invoice invoice, string clientCompanyName) =>
        new(invoice.Id, invoice.ClientCompanyId, clientCompanyName, invoice.FileName, invoice.Format.ContentType(),
            invoice.SizeBytes, invoice.UploadedAt, invoice.ExtractionStatus.ToString(), invoice.ExtractionError,
            invoice.ExtractedAt,
            invoice.ExtractedData is { } data ? ExtractedInvoiceDataResponse.From(data) : null);
}

/// <summary>A copy of the domain record, so the API's JSON doesn't change when the domain does.</summary>
public sealed record ExtractedInvoiceDataResponse(
    string? SupplierName,
    string? SupplierTaxId,
    string? InvoiceNumber,
    DateOnly? IssueDate,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    string? Currency)
{
    public static ExtractedInvoiceDataResponse From(ExtractedInvoiceData data) =>
        new(data.SupplierName, data.SupplierTaxId, data.InvoiceNumber, data.IssueDate, data.NetAmount,
            data.TaxAmount, data.TotalAmount, data.Currency);
}
