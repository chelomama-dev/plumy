using Plumy.Application.Invoices.AiAccuracy;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class AiAccuracyReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private static AiAccuracySample Sample(
        string confirmed = "628", string? suggested = null,
        LedgerAccountSuggestionSource source = LedgerAccountSuggestionSource.Ai,
        Dictionary<InvoiceField, double>? confidence = null, InvoiceField[]? corrected = null) =>
        new(suggested is null ? null : LedgerAccountSuggestion.Create(suggested, source, null, Now), confirmed,
            confidence ?? [], corrected ?? []);

    [Fact]
    public void Counts_the_suggestions_kept_by_source()
    {
        var report = AiAccuracyReport.Calculate([
            Sample(confirmed: "628", suggested: "628"),
            Sample(confirmed: "622", suggested: "628"),
            Sample(confirmed: "628", suggested: "628", source: LedgerAccountSuggestionSource.SupplierHistory),
            Sample(confirmed: "628")
        ]);

        Assert.Equal(4, report.ApprovedInvoices);
        Assert.Equal(new SuggestionAccuracy(Suggested: 2, Accepted: 1), report.LedgerAccounts.Ai);
        Assert.Equal(new SuggestionAccuracy(Suggested: 1, Accepted: 1), report.LedgerAccounts.SupplierHistory);
        Assert.Equal(1, report.LedgerAccounts.NotSuggested);
    }

    [Fact]
    public void Counts_each_field_read_corrected_and_missed()
    {
        var report = AiAccuracyReport.Calculate([
            Sample(confidence: new() { [InvoiceField.NetAmount] = 0.6, [InvoiceField.TotalAmount] = 0.99 },
                corrected: [InvoiceField.NetAmount, InvoiceField.InvoiceNumber]),
            Sample(confidence: new() { [InvoiceField.NetAmount] = 0.97 })
        ]);

        var net = report.Fields.Single(f => f.Field == "netAmount");
        Assert.Equal(new FieldAccuracy("netAmount", Read: 2, Corrected: 1, Missed: 0), net);
        Assert.Equal(new FieldAccuracy("invoiceNumber", Read: 0, Corrected: 0, Missed: 1),
            report.Fields.Single(f => f.Field == "invoiceNumber"));
        Assert.Equal(Enum.GetValues<InvoiceField>().Length, report.Fields.Count);
    }

    [Fact]
    public void Leaves_invoices_without_confidences_out_of_the_fields()
    {
        // Nothing says which fields the reader found: a correction there can't be told apart from a miss
        var report = AiAccuracyReport.Calculate([Sample(corrected: [InvoiceField.TotalAmount])]);

        Assert.All(report.Fields, f => Assert.Equal((0, 0, 0), (f.Read, f.Corrected, f.Missed)));
    }

    [Fact]
    public void Puts_each_confidence_in_its_band_with_the_last_one_including_1()
    {
        var report = AiAccuracyReport.Calculate([
            Sample(
                confidence: new()
                {
                    [InvoiceField.SupplierName] = 0.49,
                    [InvoiceField.NetAmount] = 0.5,
                    [InvoiceField.TaxAmount] = 0.79,
                    [InvoiceField.TotalAmount] = 0.8,
                    [InvoiceField.IssueDate] = 1
                },
                corrected: [InvoiceField.NetAmount, InvoiceField.TaxAmount])
        ]);

        Assert.Equal(
            [
                new ConfidenceBandAccuracy(0, 0.5, Read: 1, Corrected: 0),
                new ConfidenceBandAccuracy(0.5, 0.8, Read: 2, Corrected: 2),
                new ConfidenceBandAccuracy(0.8, 0.9, Read: 1, Corrected: 0),
                new ConfidenceBandAccuracy(0.9, 0.95, Read: 0, Corrected: 0),
                new ConfidenceBandAccuracy(0.95, 1, Read: 1, Corrected: 0)
            ],
            report.ConfidenceBands);
    }

    [Fact]
    public void An_empty_period_is_all_zeros()
    {
        var report = AiAccuracyReport.Calculate([]);

        Assert.Equal(0, report.ApprovedInvoices);
        Assert.Equal(new LedgerAccountAccuracy(new(0, 0), new(0, 0), 0), report.LedgerAccounts);
        Assert.All(report.ConfidenceBands, b => Assert.Equal((0, 0), (b.Read, b.Corrected)));
    }
}

public class GetAiAccuracyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly GetAiAccuracyHandler _handler;

    public GetAiAccuracyHandlerTests() => _handler = new GetAiAccuracyHandler(_invoices);

    private async Task<Invoice> AddInvoiceAsync(
        bool approve = true, string confirmed = "628", Action<Invoice>? suggest = null)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, Guid.CreateVersion7(), "luz.pdf", InvoiceFileFormat.Pdf, 1000,
            Convert.ToHexStringLower([.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()]),
            Guid.NewGuid(), Now);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            ExtractedInvoiceData.Create(supplierName: "Endesa", totalAmount: 121m), InvoiceValidation.None, Now,
            new Dictionary<InvoiceField, double> { [InvoiceField.TotalAmount] = 0.9 });
        (suggest ?? (i => i.SuggestLedgerAccount(
            LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.Ai, null, Now))))(invoice);
        invoice.ConfirmLedgerAccount(confirmed, Guid.NewGuid(), Now);
        if (approve)
            invoice.Approve(Guid.NewGuid(), Now);
        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Reports_on_the_approved_invoices_only()
    {
        await AddInvoiceAsync();
        await AddInvoiceAsync(confirmed: "622");
        await AddInvoiceAsync(approve: false);

        var report = await _handler.HandleAsync(new GetAiAccuracyQuery(null, null, null));

        Assert.NotNull(report);
        Assert.Equal(2, report.ApprovedInvoices);
        Assert.Equal(new SuggestionAccuracy(Suggested: 2, Accepted: 1), report.LedgerAccounts.Ai);
    }

    [Fact]
    public async Task A_declined_suggestion_counts_as_not_suggested()
    {
        await AddInvoiceAsync(suggest: i => i.DeclineLedgerAccountSuggestion("No está claro."));

        var report = await _handler.HandleAsync(new GetAiAccuracyQuery(null, null, null));

        Assert.Equal((0, 1), (report!.LedgerAccounts.Ai.Suggested, report.LedgerAccounts.NotSuggested));
    }

    [Fact]
    public async Task An_unknown_company_is_not_found()
    {
        var companyId = Guid.NewGuid();
        _invoices.UnknownCompanies.Add(companyId);

        Assert.Null(await _handler.HandleAsync(new GetAiAccuracyQuery(companyId, null, null)));
    }

    [Fact]
    public async Task Rejects_a_start_after_the_end()
    {
        await Assert.ThrowsAsync<DomainException>(() => _handler.HandleAsync(
            new GetAiAccuracyQuery(null, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1))));
    }
}
