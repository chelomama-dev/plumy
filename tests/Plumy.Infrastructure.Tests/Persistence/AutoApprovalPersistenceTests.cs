using Microsoft.EntityFrameworkCore;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class AutoApprovalPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private const string SupplierTaxId = "A81948077";

    private static int _taxIdSeed;

    private async Task<Firm> AddFirmAsync()
    {
        var firm = Firm.Create("Gestoría Ruiz", Guid.NewGuid());
        await using var db = fixture.CreateDbContext(firm.Id);
        db.Firms.Add(firm);
        await db.SaveChangesAsync();
        return firm;
    }

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

    private async Task<Invoice> AddApprovedAutomaticallyAsync(ClientCompany company)
    {
        var policy = AutoApprovalPolicy.Create(true, 0.95);
        var invoice = Invoice.Create(company.FirmId, company.Id, "f.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), Now);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: SupplierTaxId, invoiceNumber: "F-42",
                issueDate: new DateOnly(2026, 10, 1), netAmount: 100m, taxAmount: 21m, totalAmount: 121m),
            InvoiceValidation.None, Now, AutoApprovalPolicy.BookedFields.ToDictionary(field => field, _ => 0.99));
        invoice.SuggestLedgerAccount(
            LedgerAccountSuggestion.Create("629", LedgerAccountSuggestionSource.SupplierHistory, null, Now));
        invoice.ApproveAutomatically(policy, Now.AddDays(1));

        await using var db = fixture.CreateDbContext(company.FirmId);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task A_firm_that_existed_before_has_it_off()
    {
        var firm = await AddFirmAsync();

        await using var db = fixture.CreateDbContext(firm.Id);
        var policy = await new FirmRepository(db).GetAutoApprovalAsync(firm.Id);

        Assert.Equal(AutoApprovalPolicy.Disabled, policy);
    }

    [Fact]
    public async Task Stores_the_firms_choice()
    {
        var firm = await AddFirmAsync();
        await using (var db = fixture.CreateDbContext(firm.Id))
        {
            var tracked = await new FirmRepository(db).GetAsync(firm.Id);
            tracked!.SetAutoApproval(AutoApprovalPolicy.Create(true, 0.9));
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(firm.Id);
        Assert.Equal(AutoApprovalPolicy.Create(true, 0.9), await new FirmRepository(read).GetAutoApprovalAsync(firm.Id));
    }

    [Fact]
    public async Task An_unknown_firm_has_it_off()
    {
        await using var db = fixture.CreateDbContext();

        Assert.Equal(AutoApprovalPolicy.Disabled, await new FirmRepository(db).GetAutoApprovalAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Keeps_that_Plumy_approved_it()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        var invoice = await AddApprovedAutomaticallyAsync(company);

        await using var db = fixture.CreateDbContext();
        var stored = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        var sample = Assert.Single(await new InvoiceRepository(db).ListAiAccuracySamplesAsync(company.Id, null, null, 10));

        Assert.True(stored.ApprovedAutomatically);
        Assert.Equal(InvoiceReviewStatus.Approved, stored.ReviewStatus);
        Assert.Null(stored.ReviewedBy);
        Assert.Null(stored.LedgerAccountConfirmedBy);
        Assert.True(sample.ApprovedAutomatically);
    }

    [Fact]
    public async Task Its_account_teaches_the_suggestions_nothing()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddApprovedAutomaticallyAsync(company);

        await using var db = fixture.CreateDbContext();
        var repository = new InvoiceRepository(db);

        Assert.Null(await repository.FindConfirmedLedgerAccountAsync(company.Id, SupplierTaxId));
        Assert.Empty(await repository.ListConfirmedLedgerAccountsAsync(company.Id, 10));
    }
}
