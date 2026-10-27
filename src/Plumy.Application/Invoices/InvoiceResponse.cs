using System.Text.Json;
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
    DateTimeOffset ExtractionQueuedAt,
    string? ExtractionError,
    DateTimeOffset? ExtractedAt,
    ExtractedInvoiceDataResponse? ExtractedData,
    IReadOnlyList<string> Issues,
    Guid? DuplicateOfInvoiceId,
    LedgerAccountSuggestionResponse? LedgerAccountSuggestion,
    LedgerAccountResponse? LedgerAccount,
    IReadOnlyDictionary<string, double> FieldConfidence,
    IReadOnlyList<string> CorrectedFields,
    string ReviewStatus,
    DateTimeOffset? ReviewedAt,
    string? RejectionReason)
{
    public static InvoiceResponse From(Invoice invoice, string clientCompanyName) =>
        new(invoice.Id, invoice.ClientCompanyId, clientCompanyName, invoice.FileName, invoice.Format.ContentType(),
            invoice.SizeBytes, invoice.UploadedAt, invoice.ExtractionStatus.ToString(), invoice.ExtractionQueuedAt,
            invoice.ExtractionError,
            invoice.ExtractedAt,
            invoice.ExtractedData is { } data ? ExtractedInvoiceDataResponse.From(data) : null,
            [.. invoice.Issues.Select(issue => issue.ToString())],
            invoice.DuplicateOfInvoiceId,
            invoice.LedgerAccountSuggestion is { } suggestion ? LedgerAccountSuggestionResponse.From(suggestion) : null,
            LedgerAccountResponse.FromCode(invoice.LedgerAccountCode),
            // Keyed like ExtractedData's JSON (camelCase), so the client looks a field up by the same name
            invoice.FieldConfidence.ToDictionary(pair => JsonName(pair.Key), pair => pair.Value),
            [.. invoice.CorrectedFields.Select(JsonName)],
            invoice.ReviewStatus.ToString(),
            invoice.ReviewedAt,
            invoice.RejectionReason);

    private static string JsonName(InvoiceField field) => JsonNamingPolicy.CamelCase.ConvertName(field.ToString());
}

/// <summary>A copy of the domain record, so the API's JSON doesn't change when the domain does.</summary>
public sealed record ExtractedInvoiceDataResponse(
    string? SupplierName,
    string? SupplierTaxId,
    string? CustomerTaxId,
    string? InvoiceNumber,
    DateOnly? IssueDate,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    string? Currency,
    string? ItemsDescription)
{
    public static ExtractedInvoiceDataResponse From(ExtractedInvoiceData data) =>
        new(data.SupplierName, data.SupplierTaxId, data.CustomerTaxId, data.InvoiceNumber, data.IssueDate, data.NetAmount,
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
