using System.ClientModel;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.Accounting;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Classification;
using static Plumy.Infrastructure.Tests.Classification.FakeChatCompletions;

namespace Plumy.Infrastructure.Tests.Classification;

public sealed class FoundryLedgerAccountClassifierTests
{
    private static readonly ExtractedInvoiceData Invoice = ExtractedInvoiceData.Create(
        supplierName: "Endesa Energía S.A.U.", supplierTaxId: "A81948077", invoiceNumber: "FE-1",
        netAmount: 100m, totalAmount: 121m, currency: "EUR", items: ["Electricidad octubre"]);

    private static readonly ConfirmedLedgerAccountExample Example = new("Movistar", "Fibra y móvil", "629");

    private readonly FakeChatCompletions _service = new();

    private Task<LedgerAccountClassification?> ClassifyAsync() =>
        new FoundryLedgerAccountClassifier(_service.CreateClient(), NullLogger<FoundryLedgerAccountClassifier>.Instance)
            .ClassifyAsync(new LedgerAccountClassificationRequest(Invoice, [Example]));

    [Fact]
    public async Task Returns_the_models_account_and_reason()
    {
        _service.Response = () => Answer("""{ "code": "628", "reason": "Es una factura de electricidad." }""");

        var result = await ClassifyAsync();

        Assert.Equal(new LedgerAccountClassification("628", "Es una factura de electricidad."), result);
    }

    [Fact]
    public async Task Calls_the_deployment_on_the_v1_api()
    {
        await ClassifyAsync();

        Assert.Equal("https://aif-test.openai.azure.com/openai/v1/chat/completions",
            _service.Request?.RequestUri?.ToString());
        Assert.Equal(Deployment, _service.RequestBody?.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Restricts_the_answer_to_the_catalog_with_a_strict_schema()
    {
        await ClassifyAsync();

        var format = _service.RequestBody!.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var jsonSchema = format.GetProperty("json_schema");
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());

        var codes = jsonSchema.GetProperty("schema").GetProperty("properties").GetProperty("code")
            .GetProperty("enum").EnumerateArray()
            .Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString())
            .ToList();
        Assert.Equal([.. ChartOfAccounts.All.Select(a => a.Code), null], codes);
    }

    [Fact]
    public async Task Sends_the_invoice_as_data_apart_from_the_instructions_and_without_identifiers()
    {
        await ClassifyAsync();

        var messages = _service.RequestBody!.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["system", "user"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Contains("628 Suministros", messages[0].GetProperty("content").ToString());

        var invoiceMessage = messages[1].GetProperty("content").ToString();
        Assert.Contains("Endesa Energía S.A.U.", invoiceMessage);
        Assert.Contains("Electricidad octubre", invoiceMessage);
        Assert.Contains("Fibra y móvil", invoiceMessage);
        // Not needed to classify, and a self-employed supplier's NIF is personal data
        Assert.DoesNotContain("A81948077", invoiceMessage);
        Assert.DoesNotContain("FE-1", invoiceMessage);
    }

    [Fact]
    public async Task The_model_not_knowing_is_no_classification()
    {
        _service.Response = () => Answer("""{ "code": null, "reason": "No hay datos suficientes." }""");

        Assert.Null(await ClassifyAsync());
    }

    [Theory]
    [InlineData("""{ "code": "6280001", "reason": "Subcuenta." }""")]
    [InlineData("""{ "reason": "Sin código." }""")]
    [InlineData("""{ "code": 628 }""")]
    [InlineData("no es JSON")]
    public async Task An_answer_outside_the_contract_is_no_classification(string content)
    {
        _service.Response = () => Answer(content);

        Assert.Null(await ClassifyAsync());
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task An_unfinished_answer_is_no_classification(string finishReason)
    {
        _service.Response = () => Answer("""{ "code": "628", "reason": "Luz." }""", finishReason);

        Assert.Null(await ClassifyAsync());
    }

    [Fact]
    public async Task A_refusal_is_no_classification()
    {
        _service.Response = () => Answer("", refusal: "No puedo ayudar con eso.");

        Assert.Null(await ClassifyAsync());
    }

    [Fact]
    public async Task A_refused_request_is_no_classification()
    {
        // e.g. the content filter on the invoice's own text: sending it again gives the same answer
        _service.Response = () => Error(HttpStatusCode.BadRequest, "content_filter");

        Assert.Null(await ClassifyAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Anything_else_throws_so_the_message_is_retried(HttpStatusCode status)
    {
        _service.Response = () => Error(status, "error");

        await Assert.ThrowsAsync<ClientResultException>(ClassifyAsync);
    }
}
