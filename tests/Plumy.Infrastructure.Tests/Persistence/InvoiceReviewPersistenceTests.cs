using Microsoft.EntityFrameworkCore;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class InvoiceReviewPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static int _taxIdSeed;

    // Each test gets its own company, so tests sharing the container never see each other's invoices
    private async Task<ClientCompany> AddCompanyAsync()
    {
        const string letters = "TRWAGMYFPDXBNJZSQVHLCKE";
        var number = Interlocked.Increment(ref _taxIdSeed) + Random.Shared.Next(0, 90_000_000);
        var company = ClientCompany.Create(fixture.Firm.Id, "Acme S.L.", TaxId.Create($"{number:D8}{letters[number % 23]}"));

        await using var db = fixture.CreateDbContext(fixture.Firm.Id);
        db.ClientCompanies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private async Task<Invoice> AddInvoiceAsync(
        ClientCompany company, DateTimeOffset uploadedAt, Action<Invoice>? change = null,
        IReadOnlyDictionary<InvoiceField, double>? confidence = null)
    {
        var invoice = Invoice.Create(company.FirmId, company.Id, "factura.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), uploadedAt);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: "F-42"),
            InvoiceValidation.None, uploadedAt, confidence);
        change?.Invoke(invoice);

        await using var db = fixture.CreateDbContext(company.FirmId);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Confidence_corrections_and_review_round_trip()
    {
        var company = await AddCompanyAsync();
        var reviewer = Guid.NewGuid();
        var invoice = await AddInvoiceAsync(company, Start,
            confidence: new Dictionary<InvoiceField, double>
            {
                [InvoiceField.SupplierName] = 0.981,
                [InvoiceField.InvoiceNumber] = 0.42
            },
            change: i =>
            {
                i.CorrectData(ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: "A81948077",
                    invoiceNumber: "F-43"), InvoiceValidation.None);
                i.Reject("Empresa equivocada", reviewer, Start);
            });

        await using var db = fixture.CreateDbContext();
        var stored = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(invoice.FieldConfidence, stored.FieldConfidence);
        Assert.Equal([InvoiceField.InvoiceNumber], stored.CorrectedFields);
        Assert.Equal(InvoiceReviewStatus.Rejected, stored.ReviewStatus);
        Assert.Equal("Empresa equivocada", stored.RejectionReason);
        Assert.Equal(reviewer, stored.ReviewedBy);
        Assert.Equal(Start, stored.ReviewedAt);
    }

    [Fact]
    public async Task Changes_to_a_tracked_invoice_are_saved()
    {
        var company = await AddCompanyAsync();
        var invoice = await AddInvoiceAsync(company, Start);

        await using (var db = fixture.CreateDbContext())
        {
            var tracked = await db.Invoices.SingleAsync(i => i.Id == invoice.Id);
            tracked.CorrectData(ExtractedInvoiceData.Create(supplierName: "Endesa S.A.", supplierTaxId: "A81948077",
                invoiceNumber: "F-42"), InvoiceValidation.None);
            tracked.ConfirmLedgerAccount("628", Guid.NewGuid(), Start);
            tracked.Approve(Guid.NewGuid(), Start);
            await db.SaveChangesAsync();
        }

        await using var check = fixture.CreateDbContext();
        var stored = await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal("Endesa S.A.", stored.ExtractedData?.SupplierName);
        Assert.Equal([InvoiceField.SupplierName], stored.CorrectedFields);
        Assert.Equal(InvoiceReviewStatus.Approved, stored.ReviewStatus);
    }

    [Fact]
    public async Task The_response_carries_the_review()
    {
        var company = await AddCompanyAsync();
        var invoice = await AddInvoiceAsync(company, Start,
            confidence: new Dictionary<InvoiceField, double> { [InvoiceField.TotalAmount] = 0.5 });

        await using var db = fixture.CreateDbContext();
        var response = await new InvoiceRepository(db).GetResponseByIdAsync(invoice.Id);

        Assert.Equal("Unreviewed", response?.ReviewStatus);
        Assert.Equal(new Dictionary<string, double> { ["totalAmount"] = 0.5 }, response?.FieldConfidence);
        Assert.Empty(response!.CorrectedFields);
    }

    [Fact]
    public async Task A_rejected_invoice_is_not_the_original_of_a_duplicate()
    {
        var company = await AddCompanyAsync();
        await AddInvoiceAsync(company, Start, i => i.Reject("Empresa equivocada", Guid.NewGuid(), Start));
        var copy = await AddInvoiceAsync(company, Start.AddHours(1));

        await using var db = fixture.CreateDbContext();
        var duplicateOf = await new InvoiceRepository(db).FindDuplicateAsync(
            copy.Id, company.Id, "A81948077", "F-42");

        Assert.Null(duplicateOf);
    }

    [Fact]
    public async Task A_rejected_invoice_teaches_no_account()
    {
        var company = await AddCompanyAsync();
        await AddInvoiceAsync(company, Start, i => i.ConfirmLedgerAccount("622", Guid.NewGuid(), Start));
        await AddInvoiceAsync(company, Start.AddHours(1), i =>
        {
            i.ConfirmLedgerAccount("628", Guid.NewGuid(), Start.AddHours(1));
            i.Reject("Empresa equivocada", Guid.NewGuid(), Start.AddHours(1));
        });

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);

        Assert.Equal("622", await repository.FindConfirmedLedgerAccountAsync(company.Id, "A81948077"));
        Assert.Equal(["622"], (await repository.ListConfirmedLedgerAccountsAsync(company.Id, 10))
            .Select(e => e.LedgerAccountCode));
    }
}
