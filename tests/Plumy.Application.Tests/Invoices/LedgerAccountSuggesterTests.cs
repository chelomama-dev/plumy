using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class LedgerAccountSuggesterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.CreateVersion7();

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly FakeClassifier _classifier = new();
    private readonly LedgerAccountSuggester _suggester;

    public LedgerAccountSuggesterTests() =>
        _suggester = new LedgerAccountSuggester(_invoices, _classifier, new FixedTimeProvider(Now));

    private static ExtractedInvoiceData Data(
        string? supplierName = "Endesa Energía S.A.U.", string? supplierTaxId = "A81948077",
        string? items = "Electricidad octubre") =>
        ExtractedInvoiceData.Create(
            supplierName: supplierName, supplierTaxId: supplierTaxId, totalAmount: 121m,
            items: items is null ? null : [items]);

    private static Invoice ReadInvoice(ExtractedInvoiceData data, Guid? companyId = null)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, companyId ?? CompanyId, "factura.pdf",
            InvoiceFileFormat.Pdf, 1000, new string('a', 64), Guid.NewGuid(), Now.AddDays(-1));
        invoice.StartExtraction();
        invoice.CompleteExtraction(data, Now.AddDays(-1));
        return invoice;
    }

    private async Task AddConfirmedAsync(
        ExtractedInvoiceData data, string code, DateTimeOffset confirmedAt, Guid? companyId = null)
    {
        var invoice = ReadInvoice(data, companyId);
        invoice.ConfirmLedgerAccount(code, Guid.NewGuid(), confirmedAt);
        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
    }

    [Fact]
    public async Task A_known_supplier_gets_its_last_confirmed_account_without_asking_the_ai()
    {
        await AddConfirmedAsync(Data(), "622", Now.AddDays(-10));
        await AddConfirmedAsync(Data(), "628", Now.AddDays(-2));

        var suggestion = await _suggester.SuggestAsync(ReadInvoice(Data()));

        Assert.NotNull(suggestion);
        Assert.Equal("628", suggestion.Code);
        Assert.Equal(LedgerAccountSuggestionSource.SupplierHistory, suggestion.Source);
        Assert.Null(suggestion.Reason);
        Assert.Empty(_classifier.Requests);
    }

    [Fact]
    public async Task Another_companys_history_is_not_used()
    {
        // Same supplier, but another client of the firm may book it differently
        await AddConfirmedAsync(Data(), "622", Now.AddDays(-1), companyId: Guid.CreateVersion7());

        var suggestion = await _suggester.SuggestAsync(ReadInvoice(Data()));

        Assert.Equal(LedgerAccountSuggestionSource.Ai, suggestion?.Source);
    }

    [Fact]
    public async Task A_new_supplier_goes_to_the_ai_with_the_companys_examples()
    {
        await AddConfirmedAsync(Data("Movistar", "A78923125", "Fibra y móvil"), "629", Now.AddDays(-3));
        await AddConfirmedAsync(Data("Otra empresa", "B00000000", "Algo"), "623", Now.AddDays(-1),
            companyId: Guid.CreateVersion7());
        var data = Data(supplierTaxId: "B99999999");

        var suggestion = await _suggester.SuggestAsync(ReadInvoice(data));

        Assert.NotNull(suggestion);
        Assert.Equal("628", suggestion.Code);
        Assert.Equal(LedgerAccountSuggestionSource.Ai, suggestion.Source);
        Assert.Equal("Es una factura de electricidad.", suggestion.Reason);
        Assert.Equal(Now, suggestion.SuggestedAt);

        var request = Assert.Single(_classifier.Requests);
        Assert.Equal(data, request.Invoice);
        var example = Assert.Single(request.Examples);
        Assert.Equal(new ConfirmedLedgerAccountExample("Movistar", "Fibra y móvil", "629"), example);
    }

    [Fact]
    public async Task Without_a_tax_id_it_goes_to_the_ai()
    {
        var suggestion = await _suggester.SuggestAsync(ReadInvoice(Data(supplierTaxId: null)));

        Assert.Equal(LedgerAccountSuggestionSource.Ai, suggestion?.Source);
    }

    [Fact]
    public async Task Nothing_that_says_what_was_bought_means_no_suggestion()
    {
        var suggestion = await _suggester.SuggestAsync(ReadInvoice(Data(supplierName: null, items: null)));

        Assert.Null(suggestion);
        Assert.Empty(_classifier.Requests);
    }

    [Fact]
    public async Task The_ai_not_knowing_means_no_suggestion()
    {
        _classifier.Result = null;

        Assert.Null(await _suggester.SuggestAsync(ReadInvoice(Data())));
    }

    [Fact]
    public async Task An_account_outside_the_catalog_is_never_suggested()
    {
        _classifier.Result = new LedgerAccountClassification("6280001", "Subcuenta inventada.");

        Assert.Null(await _suggester.SuggestAsync(ReadInvoice(Data())));
    }

    [Fact]
    public async Task An_invoice_not_read_yet_gets_nothing()
    {
        var invoice = Invoice.Create(TestTenant.FirmId, CompanyId, "factura.pdf", InvoiceFileFormat.Pdf, 1000,
            new string('a', 64), Guid.NewGuid(), Now);

        Assert.Null(await _suggester.SuggestAsync(invoice));
        Assert.Empty(_classifier.Requests);
    }

    [Fact]
    public async Task Examples_are_capped()
    {
        for (var i = 0; i < LedgerAccountSuggester.MaxExamples + 5; i++)
            await AddConfirmedAsync(Data($"Proveedor {i}", $"B{i:D8}"), "629", Now.AddDays(-i - 1));

        await _suggester.SuggestAsync(ReadInvoice(Data(supplierTaxId: "B99999999")));

        Assert.Equal(LedgerAccountSuggester.MaxExamples, Assert.Single(_classifier.Requests).Examples.Count);
    }
}
