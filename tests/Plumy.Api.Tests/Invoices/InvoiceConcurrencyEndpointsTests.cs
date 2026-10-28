using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Invoices;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Tests.Invoices;

/// <summary>ETag and If-Match: a change made from an out-of-date screen is refused with 412.</summary>
[Collection(ApiCollection.Name)]
public class InvoiceConcurrencyEndpointsTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

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

    private async Task<Guid> UploadReadInvoiceAsync()
    {
        var file = new ByteArrayContent([.. "%PDF-1.7\n"u8, .. Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await _client.PostAsync($"/api/client-companies/{_company.Id}/invoices",
            new MultipartFormDataContent { { file, "file", "factura.pdf" } });
        response.EnsureSuccessStatusCode();
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;

        await factory.UpdateInvoiceAsync(invoice.Id, i =>
        {
            i.StartExtraction();
            i.CompleteExtraction(ExtractedInvoiceData.Create(supplierName: "Endesa", totalAmount: 121m),
                InvoiceValidation.None, Now);
        });
        return invoice.Id;
    }

    private async Task<(InvoiceResponse Invoice, EntityTagHeaderValue ETag)> GetAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/invoices/{id}");
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<InvoiceResponse>())!, response.Headers.ETag!);
    }

    private Task<HttpResponseMessage> PutAsync(Guid id, string path, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/invoices/{id}/{path}")
        {
            Content = JsonContent.Create(body)
        };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request);
    }

    private static object Data(decimal total) => new { supplierName = "Endesa", totalAmount = total };

    [Fact]
    public async Task The_version_comes_in_the_body_and_as_a_strong_ETag()
    {
        var id = await UploadReadInvoiceAsync();

        var (invoice, etag) = await GetAsync(id);

        Assert.False(etag.IsWeak);
        Assert.Equal($"\"{invoice.Version}\"", etag.Tag);
        Assert.Matches("^[0-9A-F]{16}$", invoice.Version);
    }

    [Fact]
    public async Task A_change_on_the_current_version_works_and_returns_the_new_one()
    {
        var id = await UploadReadInvoiceAsync();
        var (before, etag) = await GetAsync(id);

        var response = await PutAsync(id, "data", Data(120m), etag.Tag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.NotEqual(before.Version, after?.Version);
        Assert.Equal($"\"{after?.Version}\"", response.Headers.ETag?.Tag);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("ledger-account")]
    [InlineData("review")]
    public async Task A_change_on_an_older_version_is_refused_with_412(string path)
    {
        var id = await UploadReadInvoiceAsync();
        var (_, etag) = await GetAsync(id);
        // Someone else saves meanwhile
        await factory.UpdateInvoiceAsync(id, i => i.ConfirmLedgerAccount("628", Guid.NewGuid(), Now));

        object body = path switch
        {
            "data" => Data(120m),
            "ledger-account" => new { code = "629" },
            _ => new { status = "Approved" }
        };
        var response = await PutAsync(id, path, body, etag.Tag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("changed since you loaded it", problem?.Detail);
        var (stored, _) = await GetAsync(id);
        Assert.Equal("628", stored.LedgerAccount?.Code);
        Assert.Equal(121m, stored.ExtractedData?.TotalAmount);
        Assert.Equal("Unreviewed", stored.ReviewStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    public async Task Without_a_version_to_compare_the_change_goes_through(string? ifMatch)
    {
        var id = await UploadReadInvoiceAsync();
        await factory.UpdateInvoiceAsync(id, i => i.ConfirmLedgerAccount("628", Guid.NewGuid(), Now));

        var response = await PutAsync(id, "data", Data(120m), ifMatch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // If-Match compares strongly (RFC 9110): a weak tag never matches, and a header that isn't a tag isn't skipped
    [Theory]
    [InlineData("W/\"{0}\"")]
    [InlineData("not a tag")]
    public async Task A_weak_or_malformed_tag_never_matches(string format)
    {
        var id = await UploadReadInvoiceAsync();
        var (invoice, _) = await GetAsync(id);

        var response = await PutAsync(id, "ledger-account", new { code = "628" }, string.Format(format, invoice.Version));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }
}
