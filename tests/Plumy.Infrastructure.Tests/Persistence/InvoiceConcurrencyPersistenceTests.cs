using Microsoft.EntityFrameworkCore;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Tests.Persistence;

public class InvoiceConcurrencyPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static int _taxIdSeed;

    private async Task<Invoice> AddInvoiceAsync()
    {
        const string letters = "TRWAGMYFPDXBNJZSQVHLCKE";
        var number = Interlocked.Increment(ref _taxIdSeed) + Random.Shared.Next(0, 90_000_000);
        var company = ClientCompany.Create(fixture.Firm.Id, "Acme S.L.", TaxId.Create($"{number:D8}{letters[number % 23]}"));

        var invoice = Invoice.Create(company.FirmId, company.Id, "factura.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), Start);
        invoice.StartExtraction();
        invoice.CompleteExtraction(ExtractedInvoiceData.Create(supplierName: "Endesa"), InvoiceValidation.None, Start);

        await using var db = fixture.CreateDbContext();
        db.ClientCompanies.Add(company);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task The_version_is_set_on_insert_and_changes_on_every_save()
    {
        var invoice = await AddInvoiceAsync();
        var inserted = invoice.Version;
        Assert.Equal(8, inserted.Length);
        Assert.Equal(inserted, (await ReloadAsync(invoice.Id)).Version);

        await using var db = fixture.CreateDbContext();
        var stored = await db.Invoices.SingleAsync(i => i.Id == invoice.Id);
        stored.ConfirmLedgerAccount("628", Guid.NewGuid(), Start);
        await db.SaveChangesAsync();

        Assert.NotEqual(inserted, stored.Version);
        Assert.Equal(stored.Version, (await ReloadAsync(invoice.Id)).Version);
    }

    [Fact]
    public async Task Two_saves_of_the_same_version_cannot_both_win()
    {
        var invoice = await AddInvoiceAsync();
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var mine = await first.Invoices.SingleAsync(i => i.Id == invoice.Id);
        var theirs = await second.Invoices.SingleAsync(i => i.Id == invoice.Id);

        mine.ConfirmLedgerAccount("628", Guid.NewGuid(), Start);
        await first.SaveChangesAsync();
        theirs.ConfirmLedgerAccount("629", Guid.NewGuid(), Start);

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("628", (await ReloadAsync(invoice.Id)).LedgerAccountCode);
    }

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id);
    }
}
