using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Invoices;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Tests.Invoices;

[Collection(ApiCollection.Name)]
public class ExportEndpointTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private const string ExportPath = "/api/invoices/export";
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private HttpClient _client = null!;
    private ClientCompanyResponse _company = null!;

    public async Task InitializeAsync()
    {
        _client = await factory.CreateClientInNewFirmAsync();
        _company = await CreateCompanyAsync("Acme S.L.");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ClientCompanyResponse> CreateCompanyAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/client-companies", new { name, taxId = TaxIds.NewNif() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!;
    }

    // Uploaded, then read as the worker would leave it
    private async Task<InvoiceResponse> AddReadInvoiceAsync(Guid companyId, string number, DateOnly date)
    {
        var file = new ByteArrayContent([.. "%PDF-1.7\n"u8, .. Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await _client.PostAsync($"/api/client-companies/{companyId}/invoices",
            new MultipartFormDataContent { { file, "file", $"{number}.pdf" } });
        response.EnsureSuccessStatusCode();
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;

        await factory.UpdateInvoiceAsync(invoice.Id, i =>
        {
            i.StartExtraction();
            i.CompleteExtraction(
                ExtractedInvoiceData.Create(supplierName: "Endesa", invoiceNumber: number, issueDate: date,
                    netAmount: 100m, taxAmount: 21m, totalAmount: 121m),
                InvoiceValidation.None, Now);
            // Only approved invoices are exported
            i.ConfirmLedgerAccount("628", Guid.NewGuid(), Now);
            i.Approve(Guid.NewGuid(), Now);
        });
        return invoice;
    }

    private async Task<string[]> CsvLinesAsync(HttpResponseMessage response) =>
        Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync())[1..]
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Csv_of_a_company_and_period_downloads_as_a_named_attachment()
    {
        await AddReadInvoiceAsync(_company.Id, "F-1", new DateOnly(2026, 9, 30));
        await AddReadInvoiceAsync(_company.Id, "F-2", new DateOnly(2026, 10, 15));

        var response = await _client.GetAsync(
            $"{ExportPath}?clientCompanyId={_company.Id}&from=2026-10-01&to=2026-10-31&format=csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"facturas-{_company.TaxId}_2026-10-01_2026-10-31.csv",
            response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var lines = await CsvLinesAsync(response);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Acme S.L.;", lines[1]);
        Assert.Contains(";F-2;Endesa;", lines[1]);
    }

    [Fact]
    public async Task Excel_by_default_with_every_company()
    {
        var other = await CreateCompanyAsync("Beta S.L.");
        await AddReadInvoiceAsync(_company.Id, "F-1", new DateOnly(2026, 10, 1));
        await AddReadInvoiceAsync(other.Id, "B-1", new DateOnly(2026, 10, 2));

        var response = await _client.GetAsync(ExportPath);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("facturas-todas.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var companies = workbook.Worksheet(1).Column(1).CellsUsed().Skip(1).Select(c => c.GetText());
        Assert.Equal(["Acme S.L.", "Beta S.L."], companies);
    }

    [Fact]
    public async Task Another_firms_invoices_are_never_exported()
    {
        await AddReadInvoiceAsync(_company.Id, "F-1", new DateOnly(2026, 10, 1));
        var otherFirm = await factory.CreateClientInNewFirmAsync("Otra gestoría");

        var all = await otherFirm.GetAsync($"{ExportPath}?format=csv");
        var ours = await otherFirm.GetAsync($"{ExportPath}?clientCompanyId={_company.Id}");

        Assert.Single(await CsvLinesAsync(all));
        Assert.Equal(HttpStatusCode.NotFound, ours.StatusCode);
    }

    [Theory]
    [InlineData("?format=pdf")]
    [InlineData("?from=2026-10-31&to=2026-10-01")]
    [InlineData("?from=ayer")]
    public async Task A_bad_request_returns_400(string query)
    {
        var response = await _client.GetAsync(ExportPath + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_company_returns_404()
    {
        var response = await _client.GetAsync($"{ExportPath}?clientCompanyId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Needs_a_firm_and_a_user()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientWithoutFirm().GetAsync(ExportPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(ExportPath)).StatusCode);
    }
}
