using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.ConfirmLedgerAccount;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class ConfirmLedgerAccountHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly ConfirmLedgerAccountHandler _handler;

    public ConfirmLedgerAccountHandlerTests() =>
        _handler = new ConfirmLedgerAccountHandler(
            _invoices, new TestCurrentUser(), _invoices, new FixedTimeProvider(Now));

    private async Task<Invoice> AddInvoiceAsync(bool read = true)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, Guid.CreateVersion7(), "factura.pdf",
            InvoiceFileFormat.Pdf, 1000, new string('a', 64), Guid.NewGuid(), Now.AddDays(-1));
        if (read)
        {
            invoice.StartExtraction();
            invoice.CompleteExtraction(ExtractedInvoiceData.Create(supplierName: "Endesa"), InvoiceValidation.None, Now.AddDays(-1));
        }

        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Confirms_the_account_with_who_and_when()
    {
        var invoice = await AddInvoiceAsync();

        var response = await _handler.HandleAsync(new ConfirmLedgerAccountCommand(invoice.Id, "628"));

        Assert.Equal("628", invoice.LedgerAccountCode);
        Assert.Equal(TestCurrentUser.ObjectId, invoice.LedgerAccountConfirmedBy);
        Assert.Equal(Now, invoice.LedgerAccountConfirmedAt);
        Assert.Equal(new("628", "Suministros"), response?.LedgerAccount);
    }

    [Fact]
    public async Task Unknown_invoice_is_null()
    {
        Assert.Null(await _handler.HandleAsync(new ConfirmLedgerAccountCommand(Guid.NewGuid(), "628")));
    }

    [Fact]
    public async Task An_invoice_not_read_yet_is_a_conflict()
    {
        var invoice = await AddInvoiceAsync(read: false);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _handler.HandleAsync(new ConfirmLedgerAccountCommand(invoice.Id, "628")));
    }

    [Fact]
    public async Task An_account_outside_the_catalog_is_rejected()
    {
        var invoice = await AddInvoiceAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(new ConfirmLedgerAccountCommand(invoice.Id, "999")));
        Assert.Null(invoice.LedgerAccountCode);
    }

    [Fact]
    public async Task Refuses_a_change_made_on_an_older_version()
    {
        var invoice = await AddInvoiceAsync();
        var saves = _invoices.SaveCount;

        await Assert.ThrowsAsync<PreconditionFailedException>(() =>
            _handler.HandleAsync(new ConfirmLedgerAccountCommand(invoice.Id, "628", ExpectedVersion: "00000000000007D1")));
        Assert.Null(invoice.LedgerAccountCode);
        Assert.Equal(saves, _invoices.SaveCount);
    }

    [Fact]
    public async Task Takes_a_change_made_on_the_current_version()
    {
        var invoice = await AddInvoiceAsync();

        await _handler.HandleAsync(
            new ConfirmLedgerAccountCommand(invoice.Id, "628", ExpectedVersion: InvoiceVersion.Of(invoice)));

        Assert.Equal("628", invoice.LedgerAccountCode);
    }
}
