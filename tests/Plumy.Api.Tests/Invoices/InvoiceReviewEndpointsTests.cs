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

[Collection(ApiCollection.Name)]
public class InvoiceReviewEndpointsTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

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

    private async Task<InvoiceResponse> UploadAsync(bool read = true)
    {
        var file = new ByteArrayContent([.. "%PDF-1.7\n"u8, .. Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await _client.PostAsync($"/api/client-companies/{_company.Id}/invoices",
            new MultipartFormDataContent { { file, "file", "factura.pdf" } });
        response.EnsureSuccessStatusCode();
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;

        if (read)
        {
            // What the worker leaves: a misread total, flagged, and the reader unsure of it
            await factory.UpdateInvoiceAsync(invoice.Id, i =>
            {
                i.StartExtraction();
                i.CompleteExtraction(
                    ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: "A81948077",
                        invoiceNumber: "F-42", issueDate: new DateOnly(2026, 10, 1), netAmount: 100m,
                        taxAmount: 21m, totalAmount: 12.1m, currency: "EUR"),
                    new InvoiceValidation([InvoiceIssue.AmountsMismatch], null), Now,
                    new Dictionary<InvoiceField, double> { [InvoiceField.TotalAmount] = 0.41 });
            });
        }

        return invoice;
    }

    private static object Data(decimal? total = 121m, string? currency = "EUR") => new
    {
        supplierName = "Endesa",
        supplierTaxId = "A81948077",
        customerTaxId = (string?)null,
        invoiceNumber = "F-42",
        issueDate = "2026-10-01",
        netAmount = 100m,
        taxAmount = 21m,
        totalAmount = total,
        currency,
        itemsDescription = (string?)null
    };

    private Task<HttpResponseMessage> CorrectAsync(Guid id, object data) =>
        _client.PutAsJsonAsync($"/api/invoices/{id}/data", data);

    private Task<HttpResponseMessage> ReviewAsync(Guid id, string? status, string? rejectionReason = null) =>
        _client.PutAsJsonAsync($"/api/invoices/{id}/review", new { status, rejectionReason });

    [Fact]
    public async Task A_read_invoice_comes_with_its_confidence_and_unreviewed()
    {
        var invoice = await UploadAsync();

        var stored = await _client.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{invoice.Id}");

        Assert.Equal("Unreviewed", stored?.ReviewStatus);
        Assert.Equal(new Dictionary<string, double> { ["totalAmount"] = 0.41 }, stored?.FieldConfidence);
    }

    [Fact]
    public async Task Correcting_fixes_the_data_and_clears_the_issue_it_had()
    {
        var invoice = await UploadAsync();

        var response = await CorrectAsync(invoice.Id, Data());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var corrected = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(121m, corrected?.ExtractedData?.TotalAmount);
        Assert.Empty(corrected!.Issues);
        Assert.Equal(["totalAmount"], corrected.CorrectedFields);
    }

    [Fact]
    public async Task A_value_that_doesnt_fit_is_a_validation_error()
    {
        var invoice = await UploadAsync();

        var response = await CorrectAsync(invoice.Id, Data(currency: "EURO"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Contains("currency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Correcting_an_invoice_not_read_yet_is_a_conflict()
    {
        var invoice = await UploadAsync(read: false);

        var response = await CorrectAsync(invoice.Id, Data());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Approving_needs_the_account_first()
    {
        var invoice = await UploadAsync();

        var withoutAccount = await ReviewAsync(invoice.Id, "Approved");
        Assert.Equal(HttpStatusCode.BadRequest, withoutAccount.StatusCode);

        (await _client.PutAsJsonAsync($"/api/invoices/{invoice.Id}/ledger-account", new { code = "628" }))
            .EnsureSuccessStatusCode();
        var approved = await ReviewAsync(invoice.Id, "approved");

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var body = await approved.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal("Approved", body?.ReviewStatus);
        Assert.NotNull(body?.ReviewedAt);
    }

    [Fact]
    public async Task An_approved_invoice_cant_be_corrected_until_reopened()
    {
        var invoice = await UploadAsync();
        (await _client.PutAsJsonAsync($"/api/invoices/{invoice.Id}/ledger-account", new { code = "628" }))
            .EnsureSuccessStatusCode();
        (await ReviewAsync(invoice.Id, "Approved")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await CorrectAsync(invoice.Id, Data())).StatusCode);

        (await ReviewAsync(invoice.Id, "Unreviewed")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await CorrectAsync(invoice.Id, Data())).StatusCode);
    }

    [Fact]
    public async Task Rejecting_keeps_the_reason()
    {
        var invoice = await UploadAsync();

        var response = await ReviewAsync(invoice.Id, "Rejected", "No es una factura");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(("Rejected", "No es una factura"), (body?.ReviewStatus, body?.RejectionReason));
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_a_bad_request()
    {
        var invoice = await UploadAsync();

        var response = await ReviewAsync(invoice.Id, "Rejected");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A rejection needs a reason.",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("1")]
    [InlineData(null)]
    public async Task An_unknown_status_is_a_validation_error(string? status)
    {
        var invoice = await UploadAsync();

        var response = await ReviewAsync(invoice.Id, status);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("status", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Another_firms_invoice_is_not_found()
    {
        var invoice = await UploadAsync();
        var other = await factory.CreateClientInNewFirmAsync();

        var review = await other.PutAsJsonAsync($"/api/invoices/{invoice.Id}/review",
            new { status = "Rejected", rejectionReason = "No" });
        var correct = await other.PutAsJsonAsync($"/api/invoices/{invoice.Id}/data", Data());

        Assert.Equal(HttpStatusCode.NotFound, review.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, correct.StatusCode);
    }

    [Fact]
    public async Task The_list_can_show_only_the_invoices_to_review()
    {
        var toReview = await UploadAsync();
        var rejected = await UploadAsync();
        (await ReviewAsync(rejected.Id, "Rejected", "Duplicada")).EnsureSuccessStatusCode();

        var page = await _client.GetFromJsonAsync<Application.Common.PagedResult<InvoiceResponse>>(
            "/api/invoices?reviewStatus=Unreviewed");

        Assert.Equal([toReview.Id], page!.Items.Select(i => i.Id));
    }
}
