using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class LedgerAccountPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.NewGuid();

    private static int _taxIdSeed;

    // Each test gets its own company, so tests sharing the container never see each other's invoices
    private async Task<ClientCompany> AddCompanyAsync(Guid firmId)
    {
        const string letters = "TRWAGMYFPDXBNJZSQVHLCKE";
        var number = Interlocked.Increment(ref _taxIdSeed) + Random.Shared.Next(0, 90_000_000);
        var company = ClientCompany.Create(firmId, "Acme S.L.", TaxId.Create($"{number:D8}{letters[number % 23]}"));

        await using var db = fixture.CreateDbContext(firmId);
        db.ClientCompanies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    // Read, and optionally confirmed, as the worker and a person would leave it
    private async Task<Invoice> AddReadInvoiceAsync(
        ClientCompany company, string supplierTaxId, string? confirmedCode = null, DateTimeOffset? confirmedAt = null,
        string supplierName = "Endesa Energía S.A.U.", string items = "Electricidad")
    {
        var invoice = Invoice.Create(company.FirmId, company.Id, "factura.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), Start);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            ExtractedInvoiceData.Create(supplierName: supplierName, supplierTaxId: supplierTaxId, items: [items]),
            Start);
        if (confirmedCode is not null)
            invoice.ConfirmLedgerAccount(confirmedCode, Person, confirmedAt ?? Start);

        await using var db = fixture.CreateDbContext(company.FirmId);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<Invoice> ReloadAsync(Invoice invoice)
    {
        await using var db = fixture.CreateDbContext();
        return (await new InvoiceRepository(db).GetByIdAsync(invoice.Id))!;
    }

    private async Task UpdateAsync(Guid invoiceId, Action<Invoice> change)
    {
        await using var db = fixture.CreateDbContext();
        var invoice = (await new InvoiceRepository(db).GetByIdAsync(invoiceId))!;
        change(invoice);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ARead_invoice_has_no_suggestion_or_account()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = await AddReadInvoiceAsync(company, "A81948077");

        var loaded = await ReloadAsync(invoice);

        Assert.Null(loaded.LedgerAccountSuggestion);
        Assert.Null(loaded.LedgerAccountCode);
        Assert.True(loaded.NeedsLedgerAccountSuggestion);
    }

    [Fact]
    public async Task Suggestion_and_confirmation_round_trip()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = await AddReadInvoiceAsync(company, "A81948077");
        var suggestion = LedgerAccountSuggestion.Create(
            "628", LedgerAccountSuggestionSource.Ai, new string('ñ', LedgerAccountSuggestion.MaxReasonLength), Start);

        await UpdateAsync(invoice.Id, i => i.SuggestLedgerAccount(suggestion));
        await UpdateAsync(invoice.Id, i => i.ConfirmLedgerAccount("622", Person, Start.AddHours(1)));

        var loaded = await ReloadAsync(invoice);
        Assert.Equal(suggestion, loaded.LedgerAccountSuggestion);
        Assert.Equal("622", loaded.LedgerAccountCode);
        Assert.Equal(Person, loaded.LedgerAccountConfirmedBy);
        Assert.Equal(Start.AddHours(1), loaded.LedgerAccountConfirmedAt);
    }

    [Fact]
    public async Task Response_names_the_accounts()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = await AddReadInvoiceAsync(company, "A81948077");
        await UpdateAsync(invoice.Id, i => i.SuggestLedgerAccount(
            LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.SupplierHistory, null, Start)));

        await using var db = fixture.CreateDbContext();
        var response = await new InvoiceRepository(db).GetResponseByIdAsync(invoice.Id);

        Assert.Equal("Suministros", response?.LedgerAccountSuggestion?.Name);
        Assert.Equal("SupplierHistory", response?.LedgerAccountSuggestion?.Source);
        Assert.Null(response?.LedgerAccount);
        Assert.Equal("Electricidad", response?.ExtractedData?.ItemsDescription);
    }

    [Fact]
    public async Task Finds_the_last_confirmed_account_of_the_supplier_in_the_company()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var otherCompany = await AddCompanyAsync(fixture.Firm.Id);
        await AddReadInvoiceAsync(company, "A81948077", "622", Start.AddDays(-5));
        await AddReadInvoiceAsync(company, "A81948077", "628", Start.AddDays(-1));
        // Neither of these counts: another supplier, another company, and one not confirmed
        await AddReadInvoiceAsync(company, "B12345674", "623", Start);
        await AddReadInvoiceAsync(otherCompany, "A81948077", "629", Start);
        await AddReadInvoiceAsync(company, "A81948077");

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);

        Assert.Equal("628", await repository.FindConfirmedLedgerAccountAsync(company.Id, "A81948077"));
        Assert.Null(await repository.FindConfirmedLedgerAccountAsync(company.Id, "X0000000T"));
    }

    [Fact]
    public async Task Lists_the_companys_confirmed_accounts_newest_first()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var otherCompany = await AddCompanyAsync(fixture.Firm.Id);
        await AddReadInvoiceAsync(company, "A1", "628", Start.AddDays(-3), "Endesa", "Luz");
        await AddReadInvoiceAsync(company, "A2", "629", Start.AddDays(-1), "Movistar", "Fibra");
        await AddReadInvoiceAsync(company, "A3", "622", Start.AddDays(-2), "Talleres Pepe", "Reparación");
        await AddReadInvoiceAsync(company, "A4");
        await AddReadInvoiceAsync(otherCompany, "A5", "623", Start);

        await using var db = fixture.CreateDbContext();
        var examples = await new InvoiceRepository(db).ListConfirmedLedgerAccountsAsync(company.Id, 2);

        Assert.Equal(
            [new ConfirmedLedgerAccountExample("Movistar", "Fibra", "629"),
             new ConfirmedLedgerAccountExample("Talleres Pepe", "Reparación", "622")],
            examples);
    }

    [Fact]
    public async Task Another_firm_never_sees_the_history()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddReadInvoiceAsync(company, "A81948077", "628");

        await using var theirDb = fixture.CreateDbContext(fixture.OtherFirm.Id);
        var repository = new InvoiceRepository(theirDb);

        Assert.Null(await repository.FindConfirmedLedgerAccountAsync(company.Id, "A81948077"));
        Assert.Empty(await repository.ListConfirmedLedgerAccountsAsync(company.Id, 20));
    }
}
