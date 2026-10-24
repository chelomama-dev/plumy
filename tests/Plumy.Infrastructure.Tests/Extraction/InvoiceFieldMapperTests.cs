using System.ClientModel.Primitives;
using Azure.AI.DocumentIntelligence;
using Plumy.Infrastructure.Extraction;

namespace Plumy.Infrastructure.Tests.Extraction;

// The fields are written as the service's JSON and read with the SDK's own deserializer, so the tests use exactly
// the objects the extractor gets
public sealed class InvoiceFieldMapperTests
{
    [Fact]
    public void Missing_fields_stay_empty()
    {
        var data = Map("{}");

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
    public void A_text_field_typed_otherwise_keeps_its_raw_text()
    {
        var data = Map("""{ "InvoiceId": { "type": "number", "valueNumber": 42, "content": "0042" } }""");

        Assert.Equal("0042", data.InvoiceNumber);
    }

    [Fact]
    public void A_date_that_was_not_parsed_is_left_empty()
    {
        var data = Map("""{ "InvoiceDate": { "type": "string", "valueString": "4 de marzo", "content": "4 de marzo" } }""");

        Assert.Null(data.IssueDate);
    }

    [Fact]
    public void An_amount_that_is_not_a_currency_is_left_empty()
    {
        var data = Map("""{ "InvoiceTotal": { "type": "string", "valueString": "ciento veintiuno", "content": "ciento veintiuno" } }""");

        Assert.Null(data.TotalAmount);
    }

    [Fact]
    public void Keeps_the_cents_the_invoice_shows()
    {
        var data = Map(Total(0.1 + 0.2));

        Assert.Equal(0.3m, data.TotalAmount);
    }

    [Theory]
    [InlineData(1e30)]
    [InlineData(-1e30)]
    public void An_absurd_amount_is_left_empty(double amount)
    {
        var data = Map(Total(amount));

        Assert.Null(data.TotalAmount);
    }

    [Fact]
    public void The_currency_is_the_totals()
    {
        var data = Map("""
            {
              "SubTotal": { "type": "currency", "valueCurrency": { "amount": 100, "currencyCode": "USD" } },
              "InvoiceTotal": { "type": "currency", "valueCurrency": { "amount": 121, "currencyCode": "EUR" } }
            }
            """);

        Assert.Equal("EUR", data.Currency);
    }

    [Fact]
    public void Without_a_currency_on_the_total_it_takes_another_amounts()
    {
        var data = Map("""
            {
              "TotalTax": { "type": "currency", "valueCurrency": { "amount": 21, "currencyCode": "EUR" } },
              "InvoiceTotal": { "type": "currency", "valueCurrency": { "amount": 121, "currencySymbol": "€" } }
            }
            """);

        Assert.Equal("EUR", data.Currency);
    }

    [Fact]
    public void Keeps_what_each_line_says_was_bought()
    {
        var data = Map("""
            {
              "Items": {
                "type": "array",
                "valueArray": [
                  { "type": "object", "valueObject": {
                      "Description": { "type": "string", "valueString": "Gasoil A", "content": "Gasoil A" },
                      "Amount": { "type": "currency", "valueCurrency": { "amount": 600 } } } },
                  { "type": "object", "valueObject": {
                      "Amount": { "type": "currency", "valueCurrency": { "amount": 5 } } } },
                  { "type": "object", "valueObject": {
                      "Description": { "type": "string", "valueString": "Transporte", "content": "Transporte" } } }
                ]
              }
            }
            """);

        Assert.Equal("Gasoil A; Transporte", data.ItemsDescription);
    }

    [Fact]
    public void Items_that_are_not_a_list_are_ignored()
    {
        var data = Map("""{ "Items": { "type": "string", "valueString": "Gasoil", "content": "Gasoil" } }""");

        Assert.Null(data.ItemsDescription);
    }

    private static string Total(double amount) =>
        $$"""{ "InvoiceTotal": { "type": "currency", "valueCurrency": { "amount": {{amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}} } } }""";

    private static Domain.Invoices.ExtractedInvoiceData Map(string fieldsJson)
    {
        var document = ModelReaderWriter.Read<AnalyzedDocument>(
            BinaryData.FromString(FakeDocumentIntelligence.Document(fieldsJson)),
            ModelReaderWriterOptions.Json)!;
        return InvoiceFieldMapper.Map(document.Fields);
    }
}
