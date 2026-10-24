using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class InvoiceExportPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private static int _taxIdSeed;

    private async Task<ClientCompany> AddCompanyAsync(Guid firmId, string name = "Acme S.L.", bool archived = false)
    {
        const string letters = "TRWAGMYFPDXBNJZSQVHLCKE";
        var number = Interlocked.Increment(ref _taxIdSeed) + Random.Shared.Next(0, 90_000_000);
        var company = ClientCompany.Create(firmId, name, TaxId.Create($"{number:D8}{letters[number % 23]}"));
        if (archived)
            company.Archive(Start);

        await using var db = fixture.CreateDbContext(firmId);
        db.ClientCompanies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private async Task<Invoice> AddInvoiceAsync(
        ClientCompany company, string? date, string number = "F-1", bool read = true)
    {
        var invoice = Invoice.Create(company.FirmId, company.Id, $"{number}.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), Start);
        if (read)
        {
            invoice.StartExtraction();
            invoice.CompleteExtraction(
                ExtractedInvoiceData.Create(invoiceNumber: number,
                    issueDate: date is null ? null : DateOnly.Parse(date)),
                InvoiceValidation.None, Start);
        }

        await using var db = fixture.CreateDbContext(company.FirmId);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Lists_a_companys_read_invoices_by_date_then_number_with_the_company()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id, "Panadería Pérez");
        var otherCompany = await AddCompanyAsync(fixture.Firm.Id);
        var noDate = await AddInvoiceAsync(company, null, "F-0");
        var b = await AddInvoiceAsync(company, "2026-10-02", "F-2");
        var a = await AddInvoiceAsync(company, "2026-10-01", "F-9");
        var c = await AddInvoiceAsync(company, "2026-10-02", "F-10");
        await AddInvoiceAsync(company, "2026-10-01", "F-5", read: false);
        await AddInvoiceAsync(otherCompany, "2026-10-01");

        await using var db = fixture.CreateDbContext();
        var sources = await new InvoiceRepository(db).ListForExportAsync(company.Id, null, null, 100);

        // Undated last; same date by number (as text, like the accountant's ledger)
        Assert.Equal([a.Id, c.Id, b.Id, noDate.Id], sources.Select(s => s.Invoice.Id));
        Assert.All(sources, s => Assert.Equal(("Panadería Pérez", company.TaxId.Value), (s.CompanyName, s.CompanyTaxId)));
    }

    [Fact]
    public async Task A_date_range_is_inclusive_and_leaves_out_undated_invoices()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddInvoiceAsync(company, "2026-09-30", "F-1");
        var first = await AddInvoiceAsync(company, "2026-10-01", "F-2");
        var last = await AddInvoiceAsync(company, "2026-10-31", "F-3");
        await AddInvoiceAsync(company, "2026-11-01", "F-4");
        await AddInvoiceAsync(company, null, "F-5");

        await using var db = fixture.CreateDbContext();
        var sources = await new InvoiceRepository(db).ListForExportAsync(
            company.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 100);

        Assert.Equal([first.Id, last.Id], sources.Select(s => s.Invoice.Id));
    }

    [Fact]
    public async Task Every_company_of_the_firm_archived_included_and_capped()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var archived = await AddCompanyAsync(fixture.Firm.Id, archived: true);
        var mine = await AddInvoiceAsync(company, "2026-01-01");
        var old = await AddInvoiceAsync(archived, "2026-01-01");

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);
        var all = await repository.ListForExportAsync(null, null, null, 10_000);

        Assert.Contains(mine.Id, all.Select(s => s.Invoice.Id));
        Assert.Contains(old.Id, all.Select(s => s.Invoice.Id));
        Assert.Single(await repository.ListForExportAsync(null, null, null, 1));
    }

    [Fact]
    public async Task Another_firm_exports_none_of_them()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = await AddInvoiceAsync(company, "2026-10-01");

        await using var theirDb = fixture.CreateDbContext(fixture.OtherFirm.Id);
        var repository = new InvoiceRepository(theirDb);

        Assert.Empty(await repository.ListForExportAsync(company.Id, null, null, 100));
        Assert.DoesNotContain(invoice.Id,
            (await repository.ListForExportAsync(null, null, null, 10_000)).Select(s => s.Invoice.Id));
    }
}
