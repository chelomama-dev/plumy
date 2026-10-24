using System.Net;
using System.Web;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Invoices.Extraction;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Extraction;
using static Plumy.Infrastructure.Tests.Extraction.FakeDocumentIntelligence;

namespace Plumy.Infrastructure.Tests.Extraction;

public sealed class DocumentIntelligenceInvoiceExtractorTests
{
    private const string FullInvoice = """
        {
          "VendorName": { "type": "string", "valueString": "Suministros Levante S.L.", "content": "SUMINISTROS LEVANTE S.L." },
          "VendorTaxId": { "type": "string", "valueString": "B-12345674", "content": "CIF: B-12345674" },
          "InvoiceId": { "type": "string", "valueString": "FV-2026/0042", "content": "FV-2026/0042" },
          "InvoiceDate": { "type": "date", "valueDate": "2026-03-04", "content": "04/03/2026" },
          "SubTotal": { "type": "currency", "valueCurrency": { "amount": 100.45, "currencySymbol": "€", "currencyCode": "EUR" } },
          "TotalTax": { "type": "currency", "valueCurrency": { "amount": 21.09, "currencySymbol": "€", "currencyCode": "EUR" } },
          "InvoiceTotal": { "type": "currency", "valueCurrency": { "amount": 121.54, "currencySymbol": "€", "currencyCode": "EUR" } }
        }
        """;

    private readonly FakeDocumentIntelligence _service = new();

    [Fact]
    public async Task Reads_every_field_of_an_invoice()
    {
        _service.PollResponse = () => Succeeded(Invoice(FullInvoice));

        var data = await ExtractAsync();

        Assert.Equal(ExtractedInvoiceData.Create(
            supplierName: "Suministros Levante S.L.",
            supplierTaxId: "B12345674",
            invoiceNumber: "FV-2026/0042",
            issueDate: new DateOnly(2026, 3, 4),
            netAmount: 100.45m,
            taxAmount: 21.09m,
            totalAmount: 121.54m,
            currency: "EUR"), data);
    }

    [Fact]
    public async Task Asks_the_prebuilt_invoice_model_with_the_spanish_locale()
    {
        _service.PollResponse = () => Succeeded(Invoice(FullInvoice));

        await ExtractAsync();

        var request = Assert.IsType<HttpRequestMessage>(_service.AnalyzeRequest);
        Assert.EndsWith("/documentModels/prebuilt-invoice:analyze", request.RequestUri!.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        Assert.Equal("es-ES", query["locale"]);
        Assert.Equal("2024-11-30", query["api-version"]);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task A_file_without_an_invoice_cannot_be_read()
    {
        _service.PollResponse = () => Succeeded("[]");

        var ex = await Assert.ThrowsAsync<InvoiceExtractionException>(ExtractAsync);

        Assert.Equal(DocumentIntelligenceInvoiceExtractor.NoInvoiceError, ex.Message);
    }

    [Fact]
    public async Task Reads_the_first_invoice_when_the_file_holds_several()
    {
        var first = Document("""{ "InvoiceId": { "type": "string", "valueString": "FIRST" } }""");
        var second = Document("""{ "InvoiceId": { "type": "string", "valueString": "SECOND" } }""");
        _service.PollResponse = () => Succeeded($"[{first}, {second}]");

        var data = await ExtractAsync();

        Assert.Equal("FIRST", data.InvoiceNumber);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "InvalidRequest")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "InvalidContentLength")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, "UnsupportedMediaType")]
    public async Task A_file_the_service_refuses_cannot_be_read(HttpStatusCode status, string code)
    {
        _service.AnalyzeResponse = () => Error(status, code);

        var ex = await Assert.ThrowsAsync<InvoiceExtractionException>(ExtractAsync);

        Assert.Equal(DocumentIntelligenceInvoiceExtractor.UnreadableError, ex.Message);
        Assert.IsType<RequestFailedException>(ex.InnerException);
    }

    [Fact]
    public async Task An_analysis_that_fails_on_the_content_cannot_be_read()
    {
        _service.PollResponse = () => FailedAnalysis("InvalidContent");

        var ex = await Assert.ThrowsAsync<InvoiceExtractionException>(ExtractAsync);

        Assert.Equal(DocumentIntelligenceInvoiceExtractor.UnreadableError, ex.Message);
    }

    // Not an InvoiceExtractionException: the worker leaves the message on the queue and it is tried again later
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "PermissionDenied")]
    public async Task Throttling_outages_and_access_problems_are_transient(HttpStatusCode status, string code)
    {
        _service.AnalyzeResponse = () => Error(status, code);

        var ex = await Assert.ThrowsAsync<RequestFailedException>(ExtractAsync);

        Assert.Equal((int)status, ex.Status);
    }

    [Fact]
    public async Task An_analysis_that_fails_inside_the_service_is_transient()
    {
        _service.PollResponse = () => FailedAnalysis("InternalServerError");

        await Assert.ThrowsAsync<RequestFailedException>(ExtractAsync);
    }

    private Task<ExtractedInvoiceData> ExtractAsync()
    {
        var extractor = new DocumentIntelligenceInvoiceExtractor(
            _service.CreateClient(), NullLogger<DocumentIntelligenceInvoiceExtractor>.Instance);
        return extractor.ExtractAsync(new MemoryStream("%PDF-1.7 test"u8.ToArray()), InvoiceFileFormat.Pdf);
    }
}
