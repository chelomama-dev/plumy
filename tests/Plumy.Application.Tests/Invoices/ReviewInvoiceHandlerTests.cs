using Plumy.Application.Common;
using Plumy.Application.Invoices.ReviewInvoice;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class ReviewInvoiceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly ReviewInvoiceHandler _handler;

    public ReviewInvoiceHandlerTests() =>
        _handler = new ReviewInvoiceHandler(_invoices, new TestCurrentUser(), _invoices, new FixedTimeProvider(Now));

    private async Task<Invoice> AddInvoiceAsync(bool read = true, bool account = true)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, Guid.CreateVersion7(), "factura.pdf",
            InvoiceFileFormat.Pdf, 1000, new string('a', 64), Guid.NewGuid(), Now.AddDays(-1));
        if (read)
        {
            invoice.StartExtraction();
            invoice.CompleteExtraction(ExtractedInvoiceData.Create(supplierName: "Endesa"), InvoiceValidation.None,
                Now.AddDays(-1));
            if (account)
                invoice.ConfirmLedgerAccount("628", Guid.NewGuid(), Now.AddDays(-1));
        }

        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    private Task<Application.Invoices.InvoiceResponse?> ReviewAsync(
        Guid id, InvoiceReviewStatus status, string? reason = null) =>
        _handler.HandleAsync(new ReviewInvoiceCommand(id, status, reason));

    [Fact]
    public async Task Approves_with_who_and_when()
    {
        var invoice = await AddInvoiceAsync();

        var response = await ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved);

        Assert.Equal(InvoiceReviewStatus.Approved, invoice.ReviewStatus);
        Assert.Equal(TestCurrentUser.ObjectId, invoice.ReviewedBy);
        Assert.Equal(Now, invoice.ReviewedAt);
        Assert.Equal("Approved", response?.ReviewStatus);
    }

    [Fact]
    public async Task Approving_twice_changes_nothing()
    {
        var invoice = await AddInvoiceAsync();
        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved);
        var saves = _invoices.SaveCount;

        var response = await ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved);

        Assert.Equal("Approved", response?.ReviewStatus);
        Assert.Equal(saves, _invoices.SaveCount);
    }

    [Fact]
    public async Task Goes_from_rejected_to_approved_without_reopening_first()
    {
        var invoice = await AddInvoiceAsync();
        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Rejected, "Duplicada");

        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved);

        Assert.Equal(InvoiceReviewStatus.Approved, invoice.ReviewStatus);
        Assert.Null(invoice.RejectionReason);
    }

    [Fact]
    public async Task Rejecting_again_with_another_reason_changes_it()
    {
        var invoice = await AddInvoiceAsync();
        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Rejected, "Duplicada");

        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Rejected, "Empresa equivocada");

        Assert.Equal("Empresa equivocada", invoice.RejectionReason);
    }

    [Fact]
    public async Task Unreviewed_reopens_it()
    {
        var invoice = await AddInvoiceAsync();
        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved);

        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Unreviewed);

        Assert.Equal(InvoiceReviewStatus.Unreviewed, invoice.ReviewStatus);
        Assert.Null(invoice.ReviewedAt);
    }

    [Fact]
    public async Task Approving_without_an_account_is_rejected_and_keeps_the_previous_state()
    {
        var invoice = await AddInvoiceAsync(account: false);
        await ReviewAsync(invoice.Id, InvoiceReviewStatus.Rejected, "Duplicada");
        var saves = _invoices.SaveCount;

        await Assert.ThrowsAsync<DomainException>(() => ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved));
        Assert.Equal(saves, _invoices.SaveCount);
    }

    [Fact]
    public async Task Approving_an_invoice_not_read_yet_is_a_conflict()
    {
        var invoice = await AddInvoiceAsync(read: false);

        await Assert.ThrowsAsync<ConflictException>(() => ReviewAsync(invoice.Id, InvoiceReviewStatus.Approved));
        await Assert.ThrowsAsync<ConflictException>(() =>
            ReviewAsync(invoice.Id, InvoiceReviewStatus.Rejected, "No es una factura"));
    }

    [Fact]
    public async Task Unknown_invoice_is_null()
    {
        Assert.Null(await ReviewAsync(Guid.NewGuid(), InvoiceReviewStatus.Approved));
    }
}
