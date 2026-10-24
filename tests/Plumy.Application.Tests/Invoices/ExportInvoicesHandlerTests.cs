using Plumy.Application.Invoices.Export;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class ExportInvoicesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly RecordingWriter _xlsx = new(InvoiceExportFormat.Xlsx, ".xlsx");
    private readonly RecordingWriter _csv = new(InvoiceExportFormat.Csv, ".csv");
    private readonly ExportInvoicesHandler _handler;

    public ExportInvoicesHandlerTests() => _handler = new ExportInvoicesHandler(_invoices, [_xlsx, _csv]);

    private async Task<Invoice> AddReadInvoiceAsync(Action<Invoice>? change = null)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, Guid.CreateVersion7(), "luz.pdf", InvoiceFileFormat.Pdf, 1000,
            new string('a', 64), Guid.NewGuid(), Now);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: "F-42",
                issueDate: new DateOnly(2026, 10, 1), netAmount: 100m, taxAmount: 21m, totalAmount: 121m,
                currency: "EUR"),
            new InvoiceValidation([InvoiceIssue.MissingIssueDate], null), Now);
        change?.Invoke(invoice);
        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    private static ExportInvoicesQuery Query(
        Guid? companyId = null, DateOnly? from = null, DateOnly? to = null,
        InvoiceExportFormat format = InvoiceExportFormat.Xlsx) => new(companyId, from, to, format);

    [Fact]
    public async Task Writes_one_row_per_read_invoice_with_what_was_read()
    {
        var invoice = await AddReadInvoiceAsync();

        var file = await _handler.HandleAsync(Query());

        Assert.NotNull(file);
        var row = Assert.Single(_xlsx.Rows!);
        Assert.Equal(new InvoiceExportRow(
            InMemoryInvoiceStore.CompanyName, InMemoryInvoiceStore.CompanyTaxId.Value, new DateOnly(2026, 10, 1),
            "F-42", "Endesa", "A81948077", 100m, 21m, 121m, "EUR", null, null, LedgerAccountStatus.None,
            row.Issues, "luz.pdf", invoice.Id), row);
        Assert.Equal([InvoiceIssue.MissingIssueDate], row.Issues);
        Assert.Equal(RecordingWriter.Content, file.Content);
        Assert.Equal("test/xlsx", file.ContentType);
        Assert.Null(_csv.Rows);
    }

    [Fact]
    public async Task Uses_the_writer_of_the_format_asked_for()
    {
        await AddReadInvoiceAsync();

        var file = await _handler.HandleAsync(Query(format: InvoiceExportFormat.Csv));

        Assert.Equal("test/csv", file?.ContentType);
        Assert.Single(_csv.Rows!);
        Assert.Null(_xlsx.Rows);
    }

    [Fact]
    public async Task A_confirmed_account_wins_over_the_suggestion()
    {
        await AddReadInvoiceAsync(i =>
        {
            i.SuggestLedgerAccount(LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.Ai, null, Now));
            i.ConfirmLedgerAccount("622", Guid.NewGuid(), Now);
        });

        await _handler.HandleAsync(Query());

        var row = Assert.Single(_xlsx.Rows!);
        Assert.Equal(("622", "Reparaciones y conservación", LedgerAccountStatus.Confirmed),
            (row.LedgerAccountCode, row.LedgerAccountName, row.LedgerAccountStatus));
    }

    [Theory]
    [InlineData(LedgerAccountSuggestionSource.Ai, LedgerAccountStatus.SuggestedByAi)]
    [InlineData(LedgerAccountSuggestionSource.SupplierHistory, LedgerAccountStatus.SuggestedByHistory)]
    public async Task An_unconfirmed_suggestion_goes_as_such(
        LedgerAccountSuggestionSource source, LedgerAccountStatus expected)
    {
        await AddReadInvoiceAsync(i => i.SuggestLedgerAccount(LedgerAccountSuggestion.Create("628", source, null, Now)));

        await _handler.HandleAsync(Query());

        var row = Assert.Single(_xlsx.Rows!);
        Assert.Equal(("628", "Suministros", expected),
            (row.LedgerAccountCode, row.LedgerAccountName, row.LedgerAccountStatus));
    }

    [Fact]
    public async Task An_unknown_company_is_null()
    {
        var companyId = Guid.NewGuid();
        _invoices.UnknownCompanies.Add(companyId);

        Assert.Null(await _handler.HandleAsync(Query(companyId)));
        Assert.Null(_xlsx.Rows);
    }

    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(Query(from: new DateOnly(2026, 10, 31), to: new DateOnly(2026, 10, 1))));
    }

    [Fact]
    public async Task An_empty_export_is_still_a_file()
    {
        var file = await _handler.HandleAsync(Query());

        Assert.NotNull(file);
        Assert.Empty(_xlsx.Rows!);
    }

    [Theory]
    [InlineData(null, null, null, "facturas-todas.xlsx")]
    [InlineData("B12345674", "2026-10-01", "2026-10-31", "facturas-B12345674_2026-10-01_2026-10-31.xlsx")]
    [InlineData(null, "2026-10-01", null, "facturas-todas_desde-2026-10-01.xlsx")]
    [InlineData(null, null, "2026-10-31", "facturas-todas_hasta-2026-10-31.xlsx")]
    public void Names_the_file_after_the_company_and_the_period(
        string? taxId, string? from, string? to, string expected)
    {
        var query = Query(from: from is null ? null : DateOnly.Parse(from), to: to is null ? null : DateOnly.Parse(to));

        Assert.Equal(expected, ExportInvoicesHandler.FileName(query, taxId, ".xlsx"));
    }

    private sealed class RecordingWriter(InvoiceExportFormat format, string extension) : IInvoiceExportWriter
    {
        public static readonly byte[] Content = [1, 2, 3];

        public IReadOnlyList<InvoiceExportRow>? Rows { get; private set; }

        public InvoiceExportFormat Format => format;
        public string ContentType => $"test/{extension[1..]}";
        public string Extension => extension;

        public async Task WriteAsync(
            IReadOnlyList<InvoiceExportRow> rows, Stream output, CancellationToken cancellationToken = default)
        {
            Rows = rows;
            await output.WriteAsync(Content, cancellationToken);
        }
    }
}
