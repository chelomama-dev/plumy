using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Domain.Invoices;

namespace Plumy.Api.Tests.Invoices;

/// <summary>
/// Each test works in a firm of its own, so lists and duplicate checks never see another test's invoices.
/// </summary>
[Collection(ApiCollection.Name)]
public class InvoiceEndpointsTests(PlumyApiFactory factory) : IAsyncLifetime
{
    private const string BasePath = "/api/invoices";

    private HttpClient _client = null!;
    private ClientCompanyResponse _company = null!;

    public async Task InitializeAsync()
    {
        _client = await factory.CreateClientInNewFirmAsync();
        _company = await CreateCompanyAsync(_client, "Acme S.L.");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<ClientCompanyResponse> CreateCompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/client-companies", new { name, taxId = TaxIds.NewNif() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientCompanyResponse>())!;
    }

    private static byte[] Pdf(string text = "factura") => [.. "%PDF-1.7\n"u8, .. Encoding.UTF8.GetBytes(text)];

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid companyId, byte[] content, string fileName = "factura.pdf",
        string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        return client.PostAsync($"/api/client-companies/{companyId}/invoices", form);
    }

    private async Task<InvoiceResponse> UploadOkAsync(byte[] content, string fileName = "factura.pdf")
    {
        var response = await UploadAsync(_client, _company.Id, content, fileName);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail;

    [Fact]
    public async Task Upload_Pdf_Returns201WithLocationAndBody()
    {
        var content = Pdf();

        var response = await UploadAsync(_client, _company.Id, content, "Factura Ñandú 0042.pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.NotNull(invoice);
        Assert.Equal(_company.Id, invoice.ClientCompanyId);
        Assert.Equal("Acme S.L.", invoice.ClientCompanyName);
        Assert.Equal("Factura Ñandú 0042.pdf", invoice.FileName);
        Assert.Equal("application/pdf", invoice.ContentType);
        Assert.Equal(content.Length, invoice.SizeBytes);
        Assert.Equal($"{BasePath}/{invoice.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Get_AfterUpload_ReturnsSameInvoice()
    {
        var uploaded = await UploadOkAsync(Pdf());

        var invoice = await _client.GetFromJsonAsync<InvoiceResponse>($"{BasePath}/{uploaded.Id}");

        // Equivalent, not Equal: a record compares its lists (Issues) by reference
        Assert.Equivalent(uploaded, invoice, strict: true);
    }

    [Fact]
    public async Task GetFile_ReturnsTheExactBytesAsAnAttachment()
    {
        var content = Pdf("contenido único");
        var uploaded = await UploadOkAsync(content, "Factura Ñandú.pdf");

        var response = await _client.GetAsync($"{BasePath}/{uploaded.Id}/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Factura Ñandú.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Upload_TypeComesFromTheContent_NotFromTheClient()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

        var response = await UploadAsync(_client, _company.Id, png, "factura.pdf", "application/pdf");

        var invoice = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal("image/png", invoice?.ContentType);
    }

    [Fact]
    public async Task Upload_TextFileDisguisedAsPdf_Returns400()
    {
        var response = await UploadAsync(_client, _company.Id, "<script>alert(1)</script>"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Only PDF, JPEG and PNG files are accepted.", await ProblemDetailAsync(response));
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns400()
    {
        var response = await UploadAsync(_client, _company.Id, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_FileOverTheLimit_Returns400WithTheLimit()
    {
        var content = new byte[Invoice.MaxFileSizeBytes + 1];
        "%PDF-"u8.CopyTo(content);

        var response = await UploadAsync(_client, _company.Id, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The file cannot exceed 10 MB.", await ProblemDetailAsync(response));
    }

    [Fact]
    public async Task Upload_WithoutFile_Returns400()
    {
        var response = await _client.PostAsync($"/api/client-companies/{_company.Id}/invoices",
            new MultipartFormDataContent { { new StringContent("x"), "other" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_SameFileTwice_Returns409()
    {
        await UploadOkAsync(Pdf("duplicada"));

        var response = await UploadAsync(_client, _company.Id, Pdf("duplicada"), "otro nombre.pdf");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("This file has already been uploaded for 'Acme S.L.'.", await ProblemDetailAsync(response));
    }

    [Fact]
    public async Task Upload_SameFileForAnotherCompany_Returns201()
    {
        await UploadOkAsync(Pdf("compartida"));
        var other = await CreateCompanyAsync(_client, "Beta S.A.");

        var response = await UploadAsync(_client, other.Id, Pdf("compartida"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnknownCompany_Returns404()
    {
        var response = await UploadAsync(_client, Guid.NewGuid(), Pdf());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ArchivedCompany_Returns404()
    {
        var archived = await CreateCompanyAsync(_client, "Archivada S.L.");
        (await _client.DeleteAsync($"/api/client-companies/{archived.Id}")).EnsureSuccessStatusCode();

        var response = await UploadAsync(_client, archived.Id, Pdf());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_IsNewestFirst_AndFiltersByCompany()
    {
        var beta = await CreateCompanyAsync(_client, "Beta S.A.");
        var first = await UploadOkAsync(Pdf("1"));
        var second = (await (await UploadAsync(_client, beta.Id, Pdf("2"))).Content.ReadFromJsonAsync<InvoiceResponse>())!;
        var third = await UploadOkAsync(Pdf("3"));

        var all = await _client.GetFromJsonAsync<PagedResult<InvoiceResponse>>(BasePath);
        var ofAcme = await _client.GetFromJsonAsync<PagedResult<InvoiceResponse>>(
            $"{BasePath}?clientCompanyId={_company.Id}");

        Assert.Equal([third.Id, second.Id, first.Id], all!.Items.Select(i => i.Id));
        Assert.Equal(3, all.TotalCount);
        Assert.Equal([third.Id, first.Id], ofAcme!.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_WithInvalidPaging_Returns400(string query)
    {
        var response = await _client.GetAsync($"{BasePath}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{BasePath}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"{BasePath}/{Guid.NewGuid()}/file")).StatusCode);
    }

    [Fact]
    public async Task AnotherFirm_CantSeeDownloadOrUploadToTheseInvoices()
    {
        var uploaded = await UploadOkAsync(Pdf("privada"));
        var otherFirm = await factory.CreateClientInNewFirmAsync("Otra gestoría");

        Assert.Equal(HttpStatusCode.NotFound, (await otherFirm.GetAsync($"{BasePath}/{uploaded.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherFirm.GetAsync($"{BasePath}/{uploaded.Id}/file")).StatusCode);
        Assert.Empty((await otherFirm.GetFromJsonAsync<PagedResult<InvoiceResponse>>(BasePath))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(otherFirm, _company.Id, Pdf())).StatusCode);
    }

    [Fact]
    public async Task Invoices_UserWithoutFirm_Returns403()
    {
        var client = factory.CreateClientWithoutFirm();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(BasePath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(client, _company.Id, Pdf())).StatusCode);
    }

    [Fact]
    public async Task Upload_Anonymous_Returns401()
    {
        var response = await UploadAsync(factory.CreateClient(), _company.Id, Pdf());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_IsPendingAndQueuedForExtraction()
    {
        var invoice = await UploadOkAsync(Pdf("a leer"));

        Assert.Equal("Pending", invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractedData);
        var request = Assert.Single(factory.ExtractionQueue.For(invoice.Id));
        Assert.NotEqual(Guid.Empty, request.FirmId);
    }

    [Fact]
    public async Task RetryExtraction_FailedInvoice_Returns202AndQueuesItAgain()
    {
        var invoice = await UploadOkAsync(Pdf("reintento"));
        await factory.UpdateInvoiceAsync(invoice.Id, i =>
        {
            i.StartExtraction();
            i.FailExtraction("Unreadable.");
        });

        var response = await _client.PostAsync($"{BasePath}/{invoice.Id}/extraction", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"{BasePath}/{invoice.Id}", response.Headers.Location?.OriginalString);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal("Pending", body!.ExtractionStatus);
        Assert.Equal("Acme S.L.", body.ClientCompanyName);
        Assert.True(body.ExtractionQueuedAt > invoice.ExtractionQueuedAt);
        Assert.Equal(2, factory.ExtractionQueue.For(invoice.Id).Count());
    }

    [Fact]
    public async Task RetryExtraction_JustQueued_Returns409()
    {
        // Right after the upload (or a retry) its message is still on its way: a second one would read it twice
        var invoice = await UploadOkAsync(Pdf("doble clic"));

        var response = await _client.PostAsync($"{BasePath}/{invoice.Id}/extraction", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(factory.ExtractionQueue.For(invoice.Id));
    }

    [Fact]
    public async Task RetryExtraction_UnknownOrAnotherFirmsInvoice_Returns404()
    {
        var invoice = await UploadOkAsync(Pdf("ajena"));
        var otherFirm = await factory.CreateClientInNewFirmAsync("Otra gestoría");

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsync($"{BasePath}/{Guid.NewGuid()}/extraction", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherFirm.PostAsync($"{BasePath}/{invoice.Id}/extraction", null)).StatusCode);
        Assert.Single(factory.ExtractionQueue.For(invoice.Id));
    }

    [Fact]
    public async Task RetryExtraction_Anonymous_Returns401()
    {
        var response = await factory.CreateClient().PostAsync($"{BasePath}/{Guid.NewGuid()}/extraction", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ShowsWhatTheChecksFound()
    {
        var first = await UploadOkAsync(Pdf("original"));
        var copy = await UploadOkAsync(Pdf("copia"));
        await factory.UpdateInvoiceAsync(copy.Id, i =>
        {
            i.StartExtraction();
            i.CompleteExtraction(ExtractedInvoiceData.Create(totalAmount: 121m),
                new InvoiceValidation([InvoiceIssue.AmountsMismatch, InvoiceIssue.PossibleDuplicate], first.Id),
                DateTimeOffset.UtcNow);
        });

        var body = await _client.GetFromJsonAsync<InvoiceResponse>($"{BasePath}/{copy.Id}");

        Assert.Equal(["AmountsMismatch", "PossibleDuplicate"], body!.Issues);
        Assert.Equal(first.Id, body.DuplicateOfInvoiceId);
        Assert.Empty(first.Issues);
    }
}
