using Plumy.Domain.Common;

namespace Plumy.Domain.Invoices;

/// <summary>What the checks of a read invoice found.</summary>
/// <param name="DuplicateOfInvoiceId">The earlier invoice it looks like a copy of, if any.</param>
public sealed record InvoiceValidation(IReadOnlyList<InvoiceIssue> Issues, Guid? DuplicateOfInvoiceId)
{
    /// <summary>Checked, nothing found.</summary>
    public static InvoiceValidation None { get; } = new([], null);
}

/// <summary>
/// The deterministic checks of a read invoice (docs/product.md): plain rules, no AI. Pure: whatever needs the
/// database (the company's NIF, an earlier invoice with the same number) comes in as arguments.
/// </summary>
public static class InvoiceValidator
{
    /// <summary>Rounding: each amount is rounded on its own, so they can be a couple of cents apart.</summary>
    public const decimal Tolerance = 0.02m;

    /// <summary>21% VAT plus the 5.2% equivalence surcharge (recargo de equivalencia), with room for rounding.</summary>
    public const decimal MaxTaxRate = 0.267m;

    /// <summary>
    /// The IRPF withholdings a supplier invoice can carry (professionals 15% or 7% for new ones, rentals 19%,
    /// some activities 1% or 2%). With one, the total is base + VAT − withholding, and that is still right.
    /// </summary>
    public static IReadOnlyList<decimal> WithholdingRates { get; } = [0.01m, 0.02m, 0.07m, 0.15m, 0.19m];

    public static InvoiceValidation Validate(
        ExtractedInvoiceData data, TaxId companyTaxId, Guid? duplicateOfInvoiceId, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(companyTaxId);

        var issues = new List<InvoiceIssue>();

        if (data.SupplierTaxId is null)
            issues.Add(InvoiceIssue.MissingSupplierTaxId);
        else if (IsSpanish(data.SupplierTaxId, out var supplierTaxId) && !TaxId.TryCreate(supplierTaxId, out _))
            issues.Add(InvoiceIssue.InvalidSupplierTaxId);

        // Only when the invoice names a customer: a receipt often doesn't, and that is fine
        if (data.CustomerTaxId is not null && WithoutSpanishPrefix(data.CustomerTaxId) != companyTaxId.Value)
            issues.Add(InvoiceIssue.CustomerTaxIdMismatch);

        if (data.InvoiceNumber is null)
            issues.Add(InvoiceIssue.MissingInvoiceNumber);

        if (data.IssueDate is not { } issueDate)
            issues.Add(InvoiceIssue.MissingIssueDate);
        // A day of slack: an invoice issued "today" somewhere ahead of us in time is not from the future
        else if (issueDate > today.AddDays(1))
            issues.Add(InvoiceIssue.FutureIssueDate);

        if (data.TotalAmount is null)
            issues.Add(InvoiceIssue.MissingTotal);

        if (data is { NetAmount: { } net, TaxAmount: { } tax, TotalAmount: { } total })
        {
            if (!AmountsAddUp(net, tax, total))
                issues.Add(InvoiceIssue.AmountsMismatch);

            // The rate is the same sign for an invoice and its credit note (both amounts negative)
            if (net != 0 && tax / net is var rate && (rate < 0 || rate > MaxTaxRate))
                issues.Add(InvoiceIssue.UnusualTaxAmount);
        }

        if (duplicateOfInvoiceId is not null)
            issues.Add(InvoiceIssue.PossibleDuplicate);

        return new InvoiceValidation(issues, duplicateOfInvoiceId);
    }

    private static bool AmountsAddUp(decimal net, decimal tax, decimal total)
    {
        var expected = net + tax;
        if (Math.Abs(total - expected) <= Tolerance)
            return true;

        return WithholdingRates.Any(rate =>
            Math.Abs(total - (expected - Math.Round(net * rate, 2, MidpointRounding.AwayFromZero))) <= Tolerance);
    }

    // Intra-community VAT numbers carry the country first: ES for Spain (ESB12345674), anything else is foreign, and
    // a foreign number has its own rules we don't check
    private static bool IsSpanish(string taxId, out string withoutPrefix)
    {
        withoutPrefix = WithoutSpanishPrefix(taxId);
        return withoutPrefix != taxId || !HasCountryPrefix(taxId);
    }

    private static string WithoutSpanishPrefix(string taxId) =>
        taxId.StartsWith("ES", StringComparison.Ordinal) && taxId.Length > 9 ? taxId[2..] : taxId;

    private static bool HasCountryPrefix(string taxId) =>
        taxId.Length > 2 && char.IsAsciiLetterUpper(taxId[0]) && char.IsAsciiLetterUpper(taxId[1]);
}
