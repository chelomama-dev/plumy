using Azure.AI.DocumentIntelligence;
using Plumy.Application.Invoices.Extraction;
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

    /// <summary>
    /// The model's confidence for each of our fields that it read, rounded to 3 decimals. A field we left empty has
    /// none, even if the model sent one: there is no value to be sure of.
    /// </summary>
    public static InvoiceExtractionResult MapWithConfidence(IReadOnlyDictionary<string, DocumentField> fields)
    {
        var data = Map(fields);

        // The currency comes with the total; the description, from the lines (the lowest line's confidence)
        var sources = new (InvoiceField Field, double? Confidence, bool HasValue)[]
        {
            (InvoiceField.SupplierName, ConfidenceOf(fields, VendorName), data.SupplierName is not null),
            (InvoiceField.SupplierTaxId, ConfidenceOf(fields, VendorTaxId), data.SupplierTaxId is not null),
            (InvoiceField.CustomerTaxId, ConfidenceOf(fields, CustomerTaxId), data.CustomerTaxId is not null),
            (InvoiceField.InvoiceNumber, ConfidenceOf(fields, InvoiceId), data.InvoiceNumber is not null),
            (InvoiceField.IssueDate, ConfidenceOf(fields, InvoiceDate), data.IssueDate is not null),
            (InvoiceField.NetAmount, ConfidenceOf(fields, SubTotal), data.NetAmount is not null),
            (InvoiceField.TaxAmount, ConfidenceOf(fields, TotalTax), data.TaxAmount is not null),
            (InvoiceField.TotalAmount, ConfidenceOf(fields, InvoiceTotal), data.TotalAmount is not null),
            (InvoiceField.Currency, ConfidenceOf(fields, InvoiceTotal), data.Currency is not null),
            (InvoiceField.ItemsDescription, LowestItemConfidence(fields), data.ItemsDescription is not null)
        };

        var confidence = sources
            .Where(source => source is { HasValue: true, Confidence: { } value } && double.IsFinite(value))
            .ToDictionary(source => source.Field, source => Math.Round(Math.Clamp(source.Confidence!.Value, 0, 1), 3));

        return new InvoiceExtractionResult(data, confidence);
    }

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

    private static double? ConfidenceOf(IReadOnlyDictionary<string, DocumentField> fields, string name) =>
        fields.GetValueOrDefault(name)?.Confidence;

    // The description joins every line's, so it is only as sure as its least sure line
    private static double? LowestItemConfidence(IReadOnlyDictionary<string, DocumentField> fields) =>
        fields.GetValueOrDefault(Items) is { ValueList: { } lines } items && items.FieldType == DocumentFieldType.List
            ? lines
                .Where(line => line.FieldType == DocumentFieldType.Dictionary && line.ValueDictionary is not null)
                .Select(line => line.ValueDictionary.GetValueOrDefault(ItemDescription)?.Confidence)
                .OfType<float>()
                .Select(value => (double?)value)
                .DefaultIfEmpty(null)
                .Min()
            : null;

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
