using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class InvoicePersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static int _taxIdSeed;

    // Each test gets its own companies, so tests sharing the container never see each other's invoices
    private async Task<ClientCompany> AddCompanyAsync(Guid firmId, string name = "Acme S.L.")
    {
        const string letters = "TRWAGMYFPDXBNJZSQVHLCKE";
        var number = Interlocked.Increment(ref _taxIdSeed) + Random.Shared.Next(0, 90_000_000);
        var company = ClientCompany.Create(firmId, name, TaxId.Create($"{number:D8}{letters[number % 23]}"));

        await using var db = fixture.CreateDbContext(firmId);
        db.ClientCompanies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static Invoice NewInvoice(ClientCompany company, DateTimeOffset uploadedAt, string? sha256 = null) =>
        Invoice.Create(company.FirmId, company.Id, "factura.pdf", InvoiceFileFormat.Pdf, 1234,
            sha256 ?? Convert.ToHexStringLower(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray()),
            Guid.NewGuid(), uploadedAt);

    private async Task SaveAsync(Guid firmId, params Invoice[] invoices)
    {
        await using var db = fixture.CreateDbContext(firmId);
        db.Invoices.AddRange(invoices);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Invoice_RoundTripsEveryField()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = NewInvoice(company, Start);
        await SaveAsync(fixture.Firm.Id, invoice);

        await using var db = fixture.CreateDbContext();
        var loaded = await new InvoiceRepository(db).GetByIdAsync(invoice.Id);

        Assert.NotNull(loaded);
        Assert.Equal(invoice.FileName, loaded.FileName);
        Assert.Equal(InvoiceFileFormat.Pdf, loaded.Format);
        Assert.Equal(1234, loaded.SizeBytes);
        Assert.Equal(invoice.Sha256, loaded.Sha256);
        Assert.Equal(invoice.StoragePath, loaded.StoragePath);
        Assert.Equal(invoice.UploadedBy, loaded.UploadedBy);
        Assert.Equal(Start, loaded.UploadedAt);
    }

    [Fact]
    public async Task List_IsNewestFirst_WithCompanyName_AndFiltersByCompany()
    {
        // A firm of its own: the list counts every invoice of the firm
        var firm = Domain.Firms.Firm.Create("Gestoría de la lista", Guid.NewGuid());
        await using (var db = fixture.CreateDbContext(firm.Id))
        {
            db.Firms.Add(firm);
            await db.SaveChangesAsync();
        }

        var acme = await AddCompanyAsync(firm.Id, "Acme S.L.");
        var beta = await AddCompanyAsync(firm.Id, "Beta S.A.");
        var old = NewInvoice(acme, Start);
        var middle = NewInvoice(beta, Start.AddMinutes(1));
        var newest = NewInvoice(acme, Start.AddMinutes(2));
        await SaveAsync(firm.Id, old, middle, newest);

        await using var readDb = fixture.CreateDbContext(firm.Id);
        var repository = new InvoiceRepository(readDb);

        var all = await repository.ListAsync(null, 1, 2);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal([newest.Id, middle.Id], all.Items.Select(i => i.Id));
        Assert.Equal(["Acme S.L.", "Beta S.A."], all.Items.Select(i => i.ClientCompanyName));

        var secondPage = await repository.ListAsync(null, 2, 2);
        Assert.Equal([old.Id], secondPage.Items.Select(i => i.Id));

        var ofAcme = await repository.ListAsync(acme.Id, 1, 10);
        Assert.Equal([newest.Id, old.Id], ofAcme.Items.Select(i => i.Id));

        Assert.Empty((await repository.ListAsync(null, 99, 10)).Items);
    }

    [Fact]
    public async Task Invoices_OfAnArchivedCompany_StayVisible()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id, "Archivada S.L.");
        var invoice = NewInvoice(company, Start);
        await SaveAsync(fixture.Firm.Id, invoice);

        await using (var db = fixture.CreateDbContext())
        {
            var tracked = await db.ClientCompanies.FindAsync(company.Id);
            tracked!.Archive(Start.AddDays(1));
            await db.SaveChangesAsync();
        }

        await using var readDb = fixture.CreateDbContext();
        var response = await new InvoiceRepository(readDb).GetResponseByIdAsync(invoice.Id);

        Assert.Equal("Archivada S.L.", response?.ClientCompanyName);
    }

    [Fact]
    public async Task OtherFirm_SeesNoneOfTheInvoices()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = NewInvoice(company, Start);
        await SaveAsync(fixture.Firm.Id, invoice);

        await using var theirDb = fixture.CreateDbContext(fixture.OtherFirm.Id);
        var repository = new InvoiceRepository(theirDb);

        Assert.Null(await repository.GetByIdAsync(invoice.Id));
        Assert.Null(await repository.GetResponseByIdAsync(invoice.Id));
        Assert.DoesNotContain(invoice.Id, (await repository.ListAsync(null, 1, 100)).Items.Select(i => i.Id));
        Assert.False(await repository.ExistsAsync(company.Id, invoice.Sha256));
    }

    [Fact]
    public async Task SameFileTwiceForSameCompany_ThrowsConflict()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var sha256 = new string('b', 64);
        await SaveAsync(fixture.Firm.Id, NewInvoice(company, Start, sha256));

        // The unique index is what really stops two uploads of the same file racing each other
        await Assert.ThrowsAsync<ConflictException>(() =>
            SaveAsync(fixture.Firm.Id, NewInvoice(company, Start, sha256)));

        await using var db = fixture.CreateDbContext();
        Assert.True(await new InvoiceRepository(db).ExistsAsync(company.Id, sha256));
    }

    [Fact]
    public async Task SameFileForTwoCompanies_IsAllowed()
    {
        var sha256 = new string('c', 64);

        await SaveAsync(fixture.Firm.Id,
            NewInvoice(await AddCompanyAsync(fixture.Firm.Id), Start, sha256),
            NewInvoice(await AddCompanyAsync(fixture.Firm.Id), Start, sha256));
    }
}
