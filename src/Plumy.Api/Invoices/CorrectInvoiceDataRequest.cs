using System.ComponentModel.DataAnnotations;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Invoices;

/// <summary>
/// Body of PUT /api/invoices/{id}/data: every field, as the person left them (null clears one). The limits are the
/// stored ones, so a value that doesn't fit is a 400 instead of being shortened or dropped without a word.
/// Public so the validation source generator sees it.
/// </summary>
public sealed record CorrectInvoiceDataRequest(
    [property: MaxLength(ExtractedInvoiceData.MaxSupplierNameLength)] string? SupplierName,
    [property: MaxLength(ExtractedInvoiceData.MaxSupplierTaxIdLength)] string? SupplierTaxId,
    [property: MaxLength(ExtractedInvoiceData.MaxSupplierTaxIdLength)] string? CustomerTaxId,
    [property: MaxLength(ExtractedInvoiceData.MaxInvoiceNumberLength)] string? InvoiceNumber,
    DateOnly? IssueDate,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    [property: RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "The currency must be an ISO 4217 code, e.g. EUR.")]
    string? Currency,
    [property: MaxLength(ExtractedInvoiceData.MaxItemsDescriptionLength)] string? ItemsDescription);
