using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Invoices;
using Plumy.Domain.Accounting;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Tests.LedgerAccounts;

[Collection(ApiCollection.Name)]
public class LedgerAccountEndpointsTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private HttpClient _client = null!;
    private ClientCompanyResponse _company = null!;

    public async Task InitializeAsync()
    {
        _client = await factory.CreateClientInNewFirmAsync();
        var response = await _client.PostAsJsonAsync("/api/client-companies",
            new { name = "Acme S.L.", taxId = TaxIds.NewNif() });
        response.EnsureSuccessStatusCode();
        _company = (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<InvoiceResponse> UploadAsync()
    {
        var file = new ByteArrayContent([.. "%PDF-1.7\n"u8, .. Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await _client.PostAsync($"/api/client-companies/{_company.Id}/invoices",
            new MultipartFormDataContent { { file, "file", "factura.pdf" } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;
    }

    // What the worker leaves: read, with the AI's suggestion
    private async Task<InvoiceResponse> UploadReadAsync()
    {
        var invoice = await UploadAsync();
        await factory.UpdateInvoiceAsync(invoice.Id, i =>
        {
            i.StartExtraction();
            i.CompleteExtraction(ExtractedInvoiceData.Create(supplierName: "Endesa", items: ["Luz"]), Now);
            i.SuggestLedgerAccount(LedgerAccountSuggestion.Create(
                "628", LedgerAccountSuggestionSource.Ai, "Es una factura de electricidad.", Now));
        });
        return invoice;
    }

    private Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid invoiceId, string? code) =>
        client.PutAsJsonAsync($"/api/invoices/{invoiceId}/ledger-account", new { code });

    [Fact]
    public async Task List_returns_the_catalog()
    {
        var accounts = await _client.GetFromJsonAsync<List<LedgerAccountResponse>>("/api/ledger-accounts");

        Assert.Equal(ChartOfAccounts.All.Select(LedgerAccountResponse.From), accounts);
    }

    [Fact]
    public async Task List_needs_a_firm()
    {
        var response = await factory.CreateClientWithoutFirm().GetAsync("/api/ledger-accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_read_invoice_shows_its_suggestion()
    {
        var invoice = await UploadReadAsync();

        var body = await _client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{invoice.Id}");

        Assert.Equal(new LedgerAccountSuggestionResponse(
            "628", "Suministros", "Ai", "Es una factura de electricidad.", Now), body?.LedgerAccountSuggestion);
        Assert.Null(body?.LedgerAccount);
    }

    [Fact]
    public async Task Confirm_sets_the_account_and_keeps_the_suggestion()
    {
        var invoice = await UploadReadAsync();

        var response = await ConfirmAsync(_client, invoice.Id, "622");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(new LedgerAccountResponse("622", "Reparaciones y conservación"), body?.LedgerAccount);
        Assert.Equal("628", body?.LedgerAccountSuggestion?.Code);
    }

    [Fact]
    public async Task Confirm_an_account_outside_the_catalog_returns_400()
    {
        var invoice = await UploadReadAsync();

        var response = await ConfirmAsync(_client, invoice.Id, "999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("999", (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail);
    }

    [Fact]
    public async Task Confirm_an_invoice_not_read_yet_returns_409()
    {
        var invoice = await UploadAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(_client, invoice.Id, "628")).StatusCode);
    }

    [Fact]
    public async Task Confirm_an_unknown_or_another_firms_invoice_returns_404()
    {
        var invoice = await UploadReadAsync();
        var otherFirm = await factory.CreateClientInNewFirmAsync("Otra gestoría");

        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(_client, Guid.NewGuid(), "628")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(otherFirm, invoice.Id, "628")).StatusCode);
    }

    [Fact]
    public async Task Confirm_anonymous_returns_401()
    {
        var response = await ConfirmAsync(factory.CreateClient(), Guid.NewGuid(), "628");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
