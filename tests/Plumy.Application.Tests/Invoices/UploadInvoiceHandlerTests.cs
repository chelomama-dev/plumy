using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Common;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.UploadInvoice;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class UploadInvoiceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryClientCompanyStore _companies = new();
    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly RecordingExtractionQueue _queue = new();
    private readonly UploadInvoiceHandler _handler;

    public UploadInvoiceHandlerTests() =>
        _handler = new UploadInvoiceHandler(
            _companies, _invoices, _storage, _invoices, new TestTenant(), new TestCurrentUser(), _queue,
            new FixedTimeProvider(Now), NullLogger<UploadInvoiceHandler>.Instance);

    private async Task<ClientCompany> AddCompanyAsync(bool archived = false)
    {
        var company = ClientCompany.Create(TestTenant.FirmId, "Acme S.L.", TaxId.Create("B12345674"));
        if (archived)
            company.Archive(Now);
        _companies.Add(company);
        await _companies.SaveChangesAsync();
        return company;
    }

    private Task<Application.Invoices.InvoiceResponse?> UploadAsync(Guid companyId, byte[] content,
        string fileName = "factura.pdf") =>
        _handler.HandleAsync(new UploadInvoiceCommand(companyId, fileName, new MemoryStream(content)));

    [Fact]
    public async Task HandleAsync_ValidPdf_StoresFileAndSavesInvoice()
    {
        var company = await AddCompanyAsync();
        var content = TestFiles.Pdf();

        var response = await UploadAsync(company.Id, content);

        Assert.NotNull(response);
        var invoice = Assert.Single(_invoices.Saved);
        Assert.Equal(invoice.Id, response.Id);
        Assert.Equal("Acme S.L.", response.ClientCompanyName);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.Equal(content.Length, response.SizeBytes);
        Assert.Equal(Now, response.UploadedAt);

        Assert.Equal(TestTenant.FirmId, invoice.FirmId);
        Assert.Equal(TestCurrentUser.ObjectId, invoice.UploadedBy);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), invoice.Sha256);

        var stored = _storage.Files[invoice.StoragePath];
        Assert.Equal(content, stored.Content);
        Assert.Equal("application/pdf", stored.ContentType);
    }

    [Fact]
    public async Task HandleAsync_DetectsFormatFromContent_NotFromName()
    {
        var company = await AddCompanyAsync();

        var response = await UploadAsync(company.Id, TestFiles.Png(), fileName: "factura.pdf");

        Assert.Equal("image/png", response!.ContentType);
        Assert.Equal(InvoiceFileFormat.Png, _invoices.Saved.Single().Format);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedFile_ThrowsAndStoresNothing()
    {
        var company = await AddCompanyAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            UploadAsync(company.Id, "Hola, soy un .txt disfrazado"u8.ToArray(), fileName: "factura.pdf"));

        Assert.Empty(_storage.Files);
        Assert.Empty(_invoices.Saved);
    }

    [Fact]
    public async Task HandleAsync_EmptyFile_Throws()
    {
        var company = await AddCompanyAsync();

        var error = await Assert.ThrowsAsync<DomainException>(() => UploadAsync(company.Id, []));

        Assert.Equal("The file is empty.", error.Message);
    }

    [Fact]
    public async Task HandleAsync_SameFileTwiceForSameCompany_ThrowsConflict()
    {
        var company = await AddCompanyAsync();
        await UploadAsync(company.Id, TestFiles.Pdf());

        await Assert.ThrowsAsync<ConflictException>(() => UploadAsync(company.Id, TestFiles.Pdf(), "copia.pdf"));

        Assert.Single(_invoices.Saved);
        Assert.Single(_storage.Files);
    }

    [Fact]
    public async Task HandleAsync_DifferentFiles_AreBothKept()
    {
        var company = await AddCompanyAsync();

        await UploadAsync(company.Id, TestFiles.Pdf("una"));
        await UploadAsync(company.Id, TestFiles.Pdf("otra"));

        Assert.Equal(2, _invoices.Saved.Count);
        Assert.Equal(2, _storage.Files.Count);
    }

    [Fact]
    public async Task HandleAsync_UnknownCompany_ReturnsNullAndStoresNothing()
    {
        var response = await UploadAsync(Guid.NewGuid(), TestFiles.Pdf());

        Assert.Null(response);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task HandleAsync_ArchivedCompany_ReturnsNull()
    {
        var company = await AddCompanyAsync(archived: true);

        Assert.Null(await UploadAsync(company.Id, TestFiles.Pdf()));
    }

    [Fact]
    public async Task HandleAsync_WhenSaveFails_DeletesTheStoredFile()
    {
        var company = await AddCompanyAsync();
        _invoices.FailNextSave = new InvalidOperationException("database down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(company.Id, TestFiles.Pdf()));

        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task HandleAsync_QueuesTheSavedInvoiceForExtraction()
    {
        var company = await AddCompanyAsync();

        var response = await UploadAsync(company.Id, TestFiles.Pdf());

        var request = Assert.Single(_queue.Requests);
        Assert.Equal((response!.Id, TestTenant.FirmId), (request.InvoiceId, request.FirmId));
        Assert.NotNull(request.AttemptId);
        Assert.Equal("Pending", response.ExtractionStatus);
    }

    [Fact]
    public async Task HandleAsync_WhenQueueingFails_StillSavesTheInvoice()
    {
        var company = await AddCompanyAsync();
        _queue.FailWith = new InvalidOperationException("Service Bus down");

        var response = await UploadAsync(company.Id, TestFiles.Pdf());

        Assert.NotNull(response);
        var invoice = Assert.Single(_invoices.Saved);
        Assert.Equal(InvoiceExtractionStatus.Pending, invoice.ExtractionStatus);
        Assert.True(_storage.Files.ContainsKey(invoice.StoragePath));
    }

    [Fact]
    public async Task HandleAsync_WhenSaveFails_QueuesNothing()
    {
        var company = await AddCompanyAsync();
        _invoices.FailNextSave = new InvalidOperationException("database down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(company.Id, TestFiles.Pdf()));

        Assert.Empty(_queue.Requests);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_QueuesOnlyTheFirst()
    {
        var company = await AddCompanyAsync();
        await UploadAsync(company.Id, TestFiles.Pdf());

        await Assert.ThrowsAsync<ConflictException>(() => UploadAsync(company.Id, TestFiles.Pdf()));

        Assert.Single(_queue.Requests);
    }
}
