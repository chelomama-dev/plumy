using System.Text.RegularExpressions;

namespace Plumy.Domain.Invoices;

/// <summary>
/// What was read from an invoice's file. Every field is optional: a reader can miss any of them, and a missing
/// field is better than a made-up one. Nothing here is validated against business rules yet (a valid NIF, base +
/// VAT = total): that is the review step, which comes later. This type only keeps the values tidy and storable.
/// </summary>
public sealed partial record ExtractedInvoiceData
{
    public const int MaxSupplierNameLength = 200;
    public const int MaxSupplierTaxIdLength = 20;
    public const int MaxInvoiceNumberLength = 50;
    public const int CurrencyLength = 3;

    /// <summary>Amounts are stored as decimal(18,2): anything this big is a misread, not an invoice.</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public string? SupplierName { get; private init; }

    /// <summary>
    /// As read, only without spaces, dots or dashes. Not a <c>TaxId</c>: it can be misread or foreign, and the
    /// invoice must still be stored so a person can fix it.
    /// </summary>
    public string? SupplierTaxId { get; private init; }

    public string? InvoiceNumber { get; private init; }
    public DateOnly? IssueDate { get; private init; }

    /// <summary>Taxable base (base imponible), before VAT.</summary>
    public decimal? NetAmount { get; private init; }

    /// <summary>Total VAT (cuota de IVA).</summary>
    public decimal? TaxAmount { get; private init; }

    public decimal? TotalAmount { get; private init; }

    /// <summary>ISO 4217 code, e.g. EUR.</summary>
    public string? Currency { get; private init; }

    // For EF Core
    private ExtractedInvoiceData()
    {
    }

    public static ExtractedInvoiceData Create(
        string? supplierName = null, string? supplierTaxId = null, string? invoiceNumber = null,
        DateOnly? issueDate = null, decimal? netAmount = null, decimal? taxAmount = null,
        decimal? totalAmount = null, string? currency = null) =>
        new()
        {
            SupplierName = Truncate(Clean(supplierName), MaxSupplierNameLength),
            SupplierTaxId = NullIfTooLong(CompactTaxId(supplierTaxId), MaxSupplierTaxIdLength),
            InvoiceNumber = NullIfTooLong(Clean(invoiceNumber), MaxInvoiceNumberLength),
            IssueDate = issueDate,
            NetAmount = Amount(netAmount),
            TaxAmount = Amount(taxAmount),
            TotalAmount = Amount(totalAmount),
            Currency = CurrencyOrNull(currency)
        };

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    // A long name is still useful shortened; it is only for display
    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: var length } && length > maxLength ? value[..maxLength].TrimEnd() : value;

    // A shortened NIF or invoice number would be a wrong one, which is worse than none
    private static string? NullIfTooLong(string? value, int maxLength) =>
        value is { Length: var length } && length > maxLength ? null : value;

    private static string? CompactTaxId(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null)
            return null;

        var compact = TaxIdSeparators().Replace(cleaned, "").ToUpperInvariant();
        return compact.Length == 0 ? null : compact;
    }

    private static decimal? Amount(decimal? value)
    {
        if (value is not { } amount)
            return null;

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) > MaxAmount ? null : rounded;
    }

    private static string? CurrencyOrNull(string? value)
    {
        var code = Clean(value)?.ToUpperInvariant();
        return code is not null && CurrencyCode().IsMatch(code) ? code : null;
    }

    [GeneratedRegex(@"[\s.\-]")]
    private static partial Regex TaxIdSeparators();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
