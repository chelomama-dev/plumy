using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class ExtractedInvoiceDataTests
{
    [Fact]
    public void Create_WithNothing_HasEveryFieldEmpty()
    {
        var data = ExtractedInvoiceData.Create();

        Assert.Null(data.SupplierName);
        Assert.Null(data.SupplierTaxId);
        Assert.Null(data.InvoiceNumber);
        Assert.Null(data.IssueDate);
        Assert.Null(data.NetAmount);
        Assert.Null(data.TaxAmount);
        Assert.Null(data.TotalAmount);
        Assert.Null(data.Currency);
    }

    [Fact]
    public void Create_KeepsValidValues()
    {
        var data = ExtractedInvoiceData.Create(
            "Papelería Sol S.L.", "B12345674", "F-2026/0042", new DateOnly(2026, 10, 1), 100m, 21m, 121m, "EUR");

        Assert.Equal("Papelería Sol S.L.", data.SupplierName);
        Assert.Equal("B12345674", data.SupplierTaxId);
        Assert.Equal("F-2026/0042", data.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 10, 1), data.IssueDate);
        Assert.Equal(100m, data.NetAmount);
        Assert.Equal(21m, data.TaxAmount);
        Assert.Equal(121m, data.TotalAmount);
        Assert.Equal("EUR", data.Currency);
    }

    [Theory]
    [InlineData("  Papelería Sol  ", "Papelería Sol")]
    [InlineData("Papelería\nSol", "PapeleríaSol")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public void Create_CleansText(string supplierName, string? expected)
    {
        Assert.Equal(expected, ExtractedInvoiceData.Create(supplierName: supplierName).SupplierName);
    }

    [Fact]
    public void Create_ShortensALongSupplierName()
    {
        var data = ExtractedInvoiceData.Create(supplierName: new string('a', 300));

        Assert.Equal(ExtractedInvoiceData.MaxSupplierNameLength, data.SupplierName!.Length);
    }

    [Theory]
    [InlineData("B-12345674", "B12345674")]
    [InlineData("b 12.345.674", "B12345674")]
    [InlineData("ES B12345674", "ESB12345674")]
    [InlineData(" - . ", null)]
    public void Create_CompactsTheTaxId(string taxId, string? expected)
    {
        Assert.Equal(expected, ExtractedInvoiceData.Create(supplierTaxId: taxId).SupplierTaxId);
    }

    [Fact]
    public void Create_DropsATooLongTaxIdOrInvoiceNumber()
    {
        // Shortened, they would be wrong values, not just incomplete ones
        var data = ExtractedInvoiceData.Create(
            supplierTaxId: new string('1', ExtractedInvoiceData.MaxSupplierTaxIdLength + 1),
            invoiceNumber: new string('1', ExtractedInvoiceData.MaxInvoiceNumberLength + 1));

        Assert.Null(data.SupplierTaxId);
        Assert.Null(data.InvoiceNumber);
    }

    [Theory]
    [InlineData(10.005, 10.01)]
    [InlineData(-10.005, -10.01)]
    [InlineData(121.4, 121.4)]
    public void Create_RoundsAmountsToCents(double amount, double expected)
    {
        Assert.Equal((decimal)expected, ExtractedInvoiceData.Create(totalAmount: (decimal)amount).TotalAmount);
    }

    [Fact]
    public void Create_DropsAnAmountThatDoesNotFit()
    {
        Assert.Null(ExtractedInvoiceData.Create(totalAmount: ExtractedInvoiceData.MaxAmount + 1).TotalAmount);
        Assert.Equal(ExtractedInvoiceData.MaxAmount,
            ExtractedInvoiceData.Create(totalAmount: ExtractedInvoiceData.MaxAmount).TotalAmount);
    }

    [Theory]
    [InlineData("eur", "EUR")]
    [InlineData(" USD ", "USD")]
    [InlineData("€", null)]
    [InlineData("EURO", null)]
    [InlineData("E1R", null)]
    public void Create_KeepsOnlyIsoCurrencyCodes(string currency, string? expected)
    {
        Assert.Equal(expected, ExtractedInvoiceData.Create(currency: currency).Currency);
    }

    [Fact]
    public void TwoReadsOfTheSameValues_AreEqual()
    {
        Assert.Equal(ExtractedInvoiceData.Create(totalAmount: 121m), ExtractedInvoiceData.Create(totalAmount: 121m));
    }
}
