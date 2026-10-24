using Azure.AI.DocumentIntelligence;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Extraction;

/// <summary>
/// Turns the fields of Document Intelligence's prebuilt invoice model into our data. Any field can be missing or
/// come with another type than expected; then it is left empty, never guessed. Tidying (lengths, rounding, NIF
/// separators) is <see cref="ExtractedInvoiceData.Create"/>'s job.
/// </summary>
/// <remarks>Field names: https://learn.microsoft.com/azure/ai-services/document-intelligence/prebuilt/invoice</remarks>
internal static class InvoiceFieldMapper
{
    internal const string VendorName = "VendorName";
    internal const string VendorTaxId = "VendorTaxId";
    internal const string CustomerTaxId = "CustomerTaxId";
    internal const string InvoiceId = "InvoiceId";
    internal const string InvoiceDate = "InvoiceDate";
    internal const string SubTotal = "SubTotal";
    internal const string TotalTax = "TotalTax";
    internal const string InvoiceTotal = "InvoiceTotal";
    internal const string Items = "Items";
    internal const string ItemDescription = "Description";

    public static ExtractedInvoiceData Map(IReadOnlyDictionary<string, DocumentField> fields)
    {
        var subTotal = Currency(fields, SubTotal);
        var totalTax = Currency(fields, TotalTax);
        var total = Currency(fields, InvoiceTotal);

        return ExtractedInvoiceData.Create(
            supplierName: Text(fields, VendorName),
            supplierTaxId: Text(fields, VendorTaxId),
            customerTaxId: Text(fields, CustomerTaxId),
            invoiceNumber: Text(fields, InvoiceId),
            issueDate: Date(fields, InvoiceDate),
            netAmount: Amount(subTotal),
            taxAmount: Amount(totalTax),
            totalAmount: Amount(total),
            // The total's currency is the invoice's; the others only if the total doesn't say
            currency: total?.CurrencyCode ?? subTotal?.CurrencyCode ?? totalTax?.CurrencyCode,
            items: ItemDescriptions(fields));
    }

    // Items is an array of objects; only each line's description is kept, to tell what the invoice is for
    private static IEnumerable<string?> ItemDescriptions(IReadOnlyDictionary<string, DocumentField> fields) =>
        fields.GetValueOrDefault(Items) is { ValueList: { } lines } items && items.FieldType == DocumentFieldType.List
            ? lines
                .Where(line => line.FieldType == DocumentFieldType.Dictionary && line.ValueDictionary is not null)
                .Select(line => Text(line.ValueDictionary, ItemDescription))
            : [];

    // A string field, or the raw text when the model typed it otherwise (e.g. an invoice number read as a number).
    // DocumentFieldType is an "extensible enum" (a struct), so it is compared, not pattern-matched
    private static string? Text(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        fields.GetValueOrDefault(name) is { } field
            ? field.FieldType == DocumentFieldType.String && field.ValueString is { } value ? value : field.Content
            : null;

    // The model returns the date already parsed (with the locale hint: 03/04 is 3 April). Raw text is no use here:
    // re-parsing it would bring back exactly the ambiguity the model resolved
    private static DateOnly? Date(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        fields.GetValueOrDefault(name) is { ValueDate: { } date } field && field.FieldType == DocumentFieldType.Date
            ? DateOnly.FromDateTime(date.DateTime)
            : null;

    private static CurrencyValue? Currency(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        fields.GetValueOrDefault(name) is { ValueCurrency: { } value } field
        && field.FieldType == DocumentFieldType.Currency
            ? value
            : null;

    // The SDK gives amounts as double. The cast to decimal rounds to 15 significant digits, so 121.55 stays 121.55
    // and not 121.549999...; but it throws on NaN, infinity or anything past decimal's range, so those (misreads
    // anyway) are dropped first, with a bound far below decimal's limit and far above any amount Create keeps
    private static decimal? Amount(CurrencyValue? value) =>
        value is { Amount: var amount } && double.IsFinite(amount) && Math.Abs(amount) < 1e20
            ? (decimal)amount
            : null;
}
