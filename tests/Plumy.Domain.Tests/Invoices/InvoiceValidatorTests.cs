using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class InvoiceValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly TaxId Company = TaxId.Create("B12345674");

    // A correct invoice: every check passes. Each test breaks one thing
    private static ExtractedInvoiceData Valid(
        string? supplierTaxId = "A81948077", string? customerTaxId = "B12345674", string? invoiceNumber = "F-42",
        DateOnly? issueDate = null, decimal? net = 100m, decimal? tax = 21m, decimal? total = 121m) =>
        ExtractedInvoiceData.Create(
            supplierName: "Endesa", supplierTaxId: supplierTaxId, customerTaxId: customerTaxId,
            invoiceNumber: invoiceNumber, issueDate: issueDate ?? new DateOnly(2026, 10, 1), netAmount: net,
            taxAmount: tax, totalAmount: total);

    private static IReadOnlyList<InvoiceIssue> Issues(ExtractedInvoiceData data, Guid? duplicateOf = null) =>
        InvoiceValidator.Validate(data, Company, duplicateOf, Today).Issues;

    [Fact]
    public void A_correct_invoice_has_no_issues()
    {
        var validation = InvoiceValidator.Validate(Valid(), Company, null, Today);

        Assert.Empty(validation.Issues);
        Assert.Null(validation.DuplicateOfInvoiceId);
    }

    [Fact]
    public void Nothing_read_lists_every_missing_field()
    {
        Assert.Equal(
            [InvoiceIssue.MissingSupplierTaxId, InvoiceIssue.MissingInvoiceNumber, InvoiceIssue.MissingIssueDate,
             InvoiceIssue.MissingTotal],
            Issues(ExtractedInvoiceData.Create()));
    }

    [Theory]
    [InlineData("A81948078")] // wrong CIF control digit
    [InlineData("12345678A")] // wrong NIF letter
    [InlineData("ESA81948078")]
    public void A_spanish_tax_id_with_a_wrong_check_digit_is_flagged(string taxId)
    {
        Assert.Equal([InvoiceIssue.InvalidSupplierTaxId], Issues(Valid(supplierTaxId: taxId)));
    }

    [Theory]
    [InlineData("ESA81948077")] // intra-community format
    [InlineData("12345678Z")]
    [InlineData("X1234567L")]
    [InlineData("FR40303265045")] // foreign: its own rules, not checked
    [InlineData("DE129273398")]
    public void Valid_or_foreign_tax_ids_are_fine(string taxId)
    {
        Assert.Empty(Issues(Valid(supplierTaxId: taxId)));
    }

    [Theory]
    [InlineData("A81948077")]
    [InlineData("ESA81948077")]
    public void An_invoice_addressed_to_another_company_is_flagged(string customerTaxId)
    {
        Assert.Equal([InvoiceIssue.CustomerTaxIdMismatch], Issues(Valid(customerTaxId: customerTaxId)));
    }

    [Theory]
    [InlineData("B12345674")]
    [InlineData("ESB12345674")]
    [InlineData(null)] // a receipt often names no customer
    public void An_invoice_addressed_to_the_company_or_to_nobody_is_fine(string? customerTaxId)
    {
        Assert.Empty(Issues(Valid(customerTaxId: customerTaxId)));
    }

    [Fact]
    public void A_date_after_tomorrow_is_flagged()
    {
        Assert.Equal([InvoiceIssue.FutureIssueDate], Issues(Valid(issueDate: Today.AddDays(2))));
        Assert.Empty(Issues(Valid(issueDate: Today.AddDays(1))));
    }

    [Theory]
    [InlineData(100, 21, 121)]
    [InlineData(100, 10, 110)]
    [InlineData(100, 4, 104)]
    [InlineData(100, 0, 100)]
    [InlineData(100.01, 21.00, 121.02)] // each amount rounded on its own
    [InlineData(1000, 210, 1060)] // professional: 15% IRPF withheld (1000 + 210 - 150)
    [InlineData(1000, 210, 1140)] // new professional: 7%
    [InlineData(1000, 210, 1020)] // rental: 19%
    [InlineData(1000, 210, 1200)] // 1%
    [InlineData(-100, -21, -121)] // credit note
    public void Amounts_that_add_up_are_fine(double net, double tax, double total)
    {
        Assert.Empty(Issues(Valid(net: (decimal)net, tax: (decimal)tax, total: (decimal)total)));
    }

    [Theory]
    [InlineData(100, 21, 118)] // 120 would be right: 1% withheld
    [InlineData(100, 21, 131)]
    [InlineData(1000, 210, 1100)] // no usual withholding gives this
    public void Amounts_that_do_not_add_up_are_flagged(double net, double tax, double total)
    {
        Assert.Contains(InvoiceIssue.AmountsMismatch,
            Issues(Valid(net: (decimal)net, tax: (decimal)tax, total: (decimal)total)));
    }

    [Fact]
    public void Without_every_amount_the_sum_is_not_checked()
    {
        Assert.Empty(Issues(Valid(tax: null, total: 999m)));
    }

    [Theory]
    [InlineData(100, 26.2, 126.2)] // 21% VAT + 5.2% equivalence surcharge: the most there is
    [InlineData(100, 15, 115)] // mixed rates average out between them
    public void Any_tax_up_to_the_highest_spanish_rate_is_fine(double net, double tax, double total)
    {
        Assert.Empty(Issues(Valid(net: (decimal)net, tax: (decimal)tax, total: (decimal)total)));
    }

    [Theory]
    [InlineData(100, 30, 130)]
    [InlineData(100, -21, 79)]
    public void Tax_no_spanish_rate_explains_is_flagged(double net, double tax, double total)
    {
        Assert.Equal([InvoiceIssue.UnusualTaxAmount],
            Issues(Valid(net: (decimal)net, tax: (decimal)tax, total: (decimal)total)));
    }

    [Fact]
    public void A_possible_duplicate_is_flagged_with_the_earlier_invoice()
    {
        var earlier = Guid.NewGuid();

        var validation = InvoiceValidator.Validate(Valid(), Company, earlier, Today);

        Assert.Equal([InvoiceIssue.PossibleDuplicate], validation.Issues);
        Assert.Equal(earlier, validation.DuplicateOfInvoiceId);
    }
}
