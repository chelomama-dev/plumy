using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.AiAccuracy;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Tests.Invoices;

[Collection(ApiCollection.Name)]
public class AiAccuracyEndpointTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private const string Path = "/api/invoices/ai-accuracy";
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

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

    // Uploaded, read with an AI suggestion, and approved with the given account
    private async Task AddApprovedInvoiceAsync(string confirmed)
    {
        var file = new ByteArrayContent([.. "%PDF-1.7\n"u8, .. Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await _client.PostAsync($"/api/client-companies/{_company.Id}/invoices",
            new MultipartFormDataContent { { file, "file", "f.pdf" } });
        response.EnsureSuccessStatusCode();
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;

        await factory.UpdateInvoiceAsync(invoice.Id, i =>
        {
            i.StartExtraction();
            i.CompleteExtraction(ExtractedInvoiceData.Create(totalAmount: 121m), InvoiceValidation.None, Now,
                new Dictionary<InvoiceField, double> { [InvoiceField.TotalAmount] = 0.97 });
            i.SuggestLedgerAccount(LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.Ai, null, Now));
            i.ConfirmLedgerAccount(confirmed, Guid.NewGuid(), Now);
            i.Approve(Guid.NewGuid(), Now);
        });
    }

    [Fact]
    public async Task Reports_how_often_the_suggestions_were_kept()
    {
        await AddApprovedInvoiceAsync("628");
        await AddApprovedInvoiceAsync("622");

        var report = await _client.GetFromJsonAsync<AiAccuracyReport>($"{Path}?clientCompanyId={_company.Id}");

        Assert.Equal(2, report!.ApprovedInvoices);
        Assert.Equal(new SuggestionAccuracy(Suggested: 2, Accepted: 1), report.LedgerAccounts.Ai);
        Assert.Equal(2, report.Fields.Single(f => f.Field == "totalAmount").Read);
        Assert.Equal(2, report.ConfidenceBands[^1].Read);
    }

    [Fact]
    public async Task A_period_with_nothing_approved_is_all_zeros()
    {
        await AddApprovedInvoiceAsync("628");

        var report = await _client.GetFromJsonAsync<AiAccuracyReport>($"{Path}?from=2020-01-01&to=2020-12-31");

        Assert.Equal(0, report!.ApprovedInvoices);
    }

    [Fact]
    public async Task Another_firms_company_is_not_found()
    {
        var other = await factory.CreateClientInNewFirmAsync();

        var response = await other.GetAsync($"{Path}?clientCompanyId={_company.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_start_after_the_end_is_a_bad_request()
    {
        var response = await _client.GetAsync($"{Path}?from=2026-10-02&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requires_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Path)).StatusCode);
    }
}
