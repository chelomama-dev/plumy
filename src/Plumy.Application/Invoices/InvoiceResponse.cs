using Plumy.Domain.Accounting;
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
    ExtractedInvoiceDataResponse? ExtractedData,
    LedgerAccountSuggestionResponse? LedgerAccountSuggestion,
    LedgerAccountResponse? LedgerAccount)
{
    public static InvoiceResponse From(Invoice invoice, string clientCompanyName) =>
        new(invoice.Id, invoice.ClientCompanyId, clientCompanyName, invoice.FileName, invoice.Format.ContentType(),
            invoice.SizeBytes, invoice.UploadedAt, invoice.ExtractionStatus.ToString(), invoice.ExtractionError,
            invoice.ExtractedAt,
            invoice.ExtractedData is { } data ? ExtractedInvoiceDataResponse.From(data) : null,
            invoice.LedgerAccountSuggestion is { } suggestion ? LedgerAccountSuggestionResponse.From(suggestion) : null,
            LedgerAccountResponse.FromCode(invoice.LedgerAccountCode));
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
    string? Currency,
    string? ItemsDescription)
{
    public static ExtractedInvoiceDataResponse From(ExtractedInvoiceData data) =>
        new(data.SupplierName, data.SupplierTaxId, data.InvoiceNumber, data.IssueDate, data.NetAmount,
            data.TaxAmount, data.TotalAmount, data.Currency, data.ItemsDescription);
}

public sealed record LedgerAccountResponse(string Code, string Name)
{
    // A code no longer in the catalog still shows: it is what the person chose
    public static LedgerAccountResponse? FromCode(string? code) =>
        code is null ? null : new(code, ChartOfAccounts.Find(code)?.Name ?? code);

    public static LedgerAccountResponse From(LedgerAccount account) => new(account.Code, account.Name);
}

/// <param name="Source">SupplierHistory or Ai.</param>
public sealed record LedgerAccountSuggestionResponse(
    string Code,
    string Name,
    string Source,
    string? Reason,
    DateTimeOffset SuggestedAt)
{
    public static LedgerAccountSuggestionResponse From(LedgerAccountSuggestion suggestion) =>
        new(suggestion.Code, ChartOfAccounts.Find(suggestion.Code)?.Name ?? suggestion.Code,
            suggestion.Source.ToString(), suggestion.Reason, suggestion.SuggestedAt);
}
