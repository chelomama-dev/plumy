using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class AiAccuracyPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static int _taxIdSeed;

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

    private async Task<Invoice> AddInvoiceAsync(
        ClientCompany company, DateTimeOffset reviewedAt, string confirmed = "628", string? suggested = "628",
        InvoiceReviewStatus review = InvoiceReviewStatus.Approved)
    {
        var invoice = Invoice.Create(company.FirmId, company.Id, "f.pdf", InvoiceFileFormat.Pdf, 1234,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), Start);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            ExtractedInvoiceData.Create(supplierName: "Endesa", netAmount: 100m), InvoiceValidation.None, Start,
            new Dictionary<InvoiceField, double> { [InvoiceField.SupplierName] = 0.99, [InvoiceField.NetAmount] = 0.6 });
        invoice.CorrectData(ExtractedInvoiceData.Create(supplierName: "Endesa", netAmount: 110m), InvoiceValidation.None);
        if (suggested is null)
            invoice.DeclineLedgerAccountSuggestion(null);
        else
            invoice.SuggestLedgerAccount(
                LedgerAccountSuggestion.Create(suggested, LedgerAccountSuggestionSource.Ai, "Luz", Start));
        invoice.ConfirmLedgerAccount(confirmed, Guid.NewGuid(), reviewedAt);
        if (review == InvoiceReviewStatus.Approved)
            invoice.Approve(Guid.NewGuid(), reviewedAt);
        else if (review == InvoiceReviewStatus.Rejected)
            invoice.Reject("Duplicada", Guid.NewGuid(), reviewedAt);

        await using var db = fixture.CreateDbContext(company.FirmId);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Reads_what_was_suggested_read_and_corrected_for_the_approved_invoices()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddInvoiceAsync(company, Start, confirmed: "622");
        await AddInvoiceAsync(company, Start, suggested: null);
        await AddInvoiceAsync(company, Start, review: InvoiceReviewStatus.Unreviewed);
        await AddInvoiceAsync(company, Start, review: InvoiceReviewStatus.Rejected);

        await using var db = fixture.CreateDbContext();
        var samples = await new InvoiceRepository(db).ListAiAccuracySamplesAsync(company.Id, null, null, 100);

        Assert.Equal(2, samples.Count);
        var suggested = Assert.Single(samples, s => s.Suggestion is not null);
        Assert.Equal(("628", LedgerAccountSuggestionSource.Ai, "622"),
            (suggested.Suggestion!.Code, suggested.Suggestion.Source, suggested.ConfirmedLedgerAccountCode));
        Assert.Equal(0.6, suggested.FieldConfidence[InvoiceField.NetAmount]);
        Assert.Equal([InvoiceField.NetAmount], suggested.CorrectedFields);
    }

    [Fact]
    public async Task A_range_takes_whole_days_of_approval()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddInvoiceAsync(company, new DateTimeOffset(2026, 10, 9, 23, 59, 0, TimeSpan.Zero));
        await AddInvoiceAsync(company, new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), confirmed: "600");
        await AddInvoiceAsync(company, new DateTimeOffset(2026, 10, 20, 23, 59, 0, TimeSpan.Zero), confirmed: "621");
        await AddInvoiceAsync(company, new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));

        await using var db = fixture.CreateDbContext();
        var samples = await new InvoiceRepository(db).ListAiAccuracySamplesAsync(
            company.Id, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 20), 100);

        Assert.Equal(["621", "600"], samples.Select(s => s.ConfirmedLedgerAccountCode));
    }

    [Fact]
    public async Task Takes_the_newest_approvals_up_to_the_maximum()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddInvoiceAsync(company, Start, confirmed: "600");
        await AddInvoiceAsync(company, Start.AddDays(1), confirmed: "621");

        await using var db = fixture.CreateDbContext();
        var samples = await new InvoiceRepository(db).ListAiAccuracySamplesAsync(company.Id, null, null, 1);

        Assert.Equal("621", Assert.Single(samples).ConfirmedLedgerAccountCode);
    }

    [Fact]
    public async Task Leaves_out_other_firms()
    {
        var company = await AddCompanyAsync(fixture.Firm.Id);
        await AddInvoiceAsync(company, Start);

        await using var db = fixture.CreateDbContext(fixture.OtherFirm.Id);
        var samples = await new InvoiceRepository(db).ListAiAccuracySamplesAsync(null, null, null, 100);

        Assert.Empty(samples);
    }
}
