using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class InvoiceValidationPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private static int _taxIdSeed;

    // Each test gets its own company, so tests sharing the container never see each other's invoices
    private async Task<ClientCompany> AddCompanyAsync(Guid firmId, bool archived = false)
    {
        const string letters = "TRWAGMYFPDXBNJZSQVHLCKE";
        var number = Interlocked.Increment(ref _taxIdSeed) + Random.Shared.Next(0, 90_000_000);
        var company = ClientCompany.Create(firmId, "Acme S.L.", TaxId.Create($"{number:D8}{letters[number % 23]}"));
        if (archived)
            company.Archive(Start);

        await using var db = fixture.CreateDbContext(firmId);
        db.ClientCompanies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private async Task<Invoice> AddInvoiceAsync(
        ClientCompany company, DateTimeOffset uploadedAt, string? supplierTaxId = "A81948077",
        string? number = "F-42", bool read = true, InvoiceValidation? validation = null)
    {
        var invoice = Invoice.Create(company.FirmId, company.Id, "factura.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), uploadedAt);
        if (read)
        {
            invoice.StartExtraction();
            invoice.CompleteExtraction(
                ExtractedInvoiceData.Create(supplierTaxId: supplierTaxId, invoiceNumber: number,
                    customerTaxId: company.TaxId.Value),
                validation ?? InvoiceValidation.None, uploadedAt);
        }

        await using var db = fixture.CreateDbContext(company.FirmId);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Issues_and_duplicate_round_trip()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var earlier = await AddInvoiceAsync(company, Start);
        var invoice = await AddInvoiceAsync(company, Start.AddHours(1), validation: new InvoiceValidation(
            [InvoiceIssue.AmountsMismatch, InvoiceIssue.PossibleDuplicate], earlier.Id));

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);
        var loaded = await repository.GetByIdAsync(invoice.Id);
        var response = await repository.GetResponseByIdAsync(invoice.Id);

        Assert.Equal([InvoiceIssue.AmountsMismatch, InvoiceIssue.PossibleDuplicate], loaded?.Issues);
        Assert.Equal(earlier.Id, loaded?.DuplicateOfInvoiceId);
        Assert.Equal(["AmountsMismatch", "PossibleDuplicate"], response?.Issues);
        Assert.Equal(company.TaxId.Value, loaded?.ExtractedData?.CustomerTaxId);
    }

    [Fact]
    public async Task An_invoice_without_issues_loads_with_none()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = await AddInvoiceAsync(company, Start, read: false);

        await using var db = fixture.CreateDbContext();

        Assert.Empty((await new InvoiceRepository(db).GetByIdAsync(invoice.Id))!.Issues);
    }

    [Fact]
    public async Task Finds_the_earliest_read_invoice_with_the_same_supplier_and_number()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var otherCompany = await AddCompanyAsync(fixture.Firm.Id);
        var first = await AddInvoiceAsync(company, Start);
        await AddInvoiceAsync(company, Start.AddHours(1));
        // None of these: another number, another supplier, another company, not read yet
        await AddInvoiceAsync(company, Start.AddMinutes(-3), number: "F-43");
        await AddInvoiceAsync(company, Start.AddMinutes(-2), supplierTaxId: "B12345674");
        await AddInvoiceAsync(otherCompany, Start.AddMinutes(-1));
        await AddInvoiceAsync(company, Start.AddMinutes(-4), read: false);
        var invoice = await AddInvoiceAsync(company, Start.AddHours(2), read: false);

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);

        Assert.Equal(first.Id, await repository.FindDuplicateAsync(invoice.Id, company.Id, "A81948077", "F-42"));
        // Never itself
        Assert.Null(await repository.FindDuplicateAsync(first.Id, otherCompany.Id, "A81948077", "F-99"));
    }

    [Fact]
    public async Task The_company_tax_id_is_found_even_if_archived()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id, archived: true);

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);

        Assert.Equal(company.TaxId, await repository.GetClientCompanyTaxIdAsync(company.Id));
        Assert.Null(await repository.GetClientCompanyTaxIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Another_firm_finds_neither()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddInvoiceAsync(company, Start);

        await using var theirDb = fixture.CreateDbContext(fixture.OtherFirm.Id);
        var repository = new InvoiceRepository(theirDb);

        Assert.Null(await repository.GetClientCompanyTaxIdAsync(company.Id));
        Assert.Null(await repository.FindDuplicateAsync(Guid.NewGuid(), company.Id, "A81948077", "F-42"));
    }
}
