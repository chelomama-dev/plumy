using Plumy.Application.Common;
using Plumy.Application.Invoices;
using Plumy.Application.Invoices.CorrectInvoiceData;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class CorrectInvoiceDataHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly CorrectInvoiceDataHandler _handler;

    public CorrectInvoiceDataHandlerTests() =>
        _handler = new CorrectInvoiceDataHandler(
            _invoices, new InvoiceChecks(_invoices, new FixedTimeProvider(Now)), _invoices);

    private async Task<Invoice> AddInvoiceAsync(
        bool read = true, Guid? companyId = null, string number = "F-42", bool approve = false)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, companyId ?? Guid.CreateVersion7(), "factura.pdf",
            InvoiceFileFormat.Pdf, 1000, new string('a', 64), Guid.NewGuid(), Now.AddDays(-1));
        if (read)
        {
            invoice.StartExtraction();
            // Misread: the total doesn't add up
            invoice.CompleteExtraction(
                ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: number,
                    issueDate: new DateOnly(2026, 10, 1), netAmount: 100m, taxAmount: 21m, totalAmount: 12.1m),
                new InvoiceValidation([InvoiceIssue.AmountsMismatch], null), Now.AddDays(-1));
        }
        if (approve)
        {
            invoice.ConfirmLedgerAccount("628", Guid.NewGuid(), Now);
            invoice.Approve(Guid.NewGuid(), Now);
        }

        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    private static CorrectInvoiceDataCommand Command(Guid id, decimal? total = 121m, string? number = "F-42") =>
        new(id, "Endesa", "A-81948077", null, number, new DateOnly(2026, 10, 1), 100m, 21m, total, "eur",
            "Electricidad");

    [Fact]
    public async Task Stores_the_tidied_values_marks_them_and_checks_them_again()
    {
        var invoice = await AddInvoiceAsync();

        var response = await _handler.HandleAsync(Command(invoice.Id));

        Assert.Equal(121m, invoice.ExtractedData?.TotalAmount);
        Assert.Equal("A81948077", invoice.ExtractedData?.SupplierTaxId);
        Assert.Equal("EUR", invoice.ExtractedData?.Currency);
        Assert.Equal("Electricidad", invoice.ExtractedData?.ItemsDescription);
        Assert.Empty(invoice.Issues);
        Assert.Equal([InvoiceField.TotalAmount, InvoiceField.Currency, InvoiceField.ItemsDescription],
            invoice.CorrectedFields);
        Assert.Equal(["totalAmount", "currency", "itemsDescription"], response?.CorrectedFields);
    }

    [Fact]
    public async Task A_correction_that_makes_it_a_copy_of_another_is_flagged()
    {
        var companyId = Guid.CreateVersion7();
        var earlier = await AddInvoiceAsync(companyId: companyId, number: "F-1");
        var invoice = await AddInvoiceAsync(companyId: companyId, number: "F-2");

        await _handler.HandleAsync(Command(invoice.Id, number: "F-1"));

        Assert.Contains(InvoiceIssue.PossibleDuplicate, invoice.Issues);
        Assert.Equal(earlier.Id, invoice.DuplicateOfInvoiceId);
    }

    [Fact]
    public async Task An_amount_out_of_range_is_rejected_not_dropped()
    {
        var invoice = await AddInvoiceAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(Command(invoice.Id, total: decimal.MaxValue)));
        Assert.Equal(12.1m, invoice.ExtractedData?.TotalAmount);
    }

    [Fact]
    public async Task Unknown_invoice_is_null()
    {
        Assert.Null(await _handler.HandleAsync(Command(Guid.NewGuid())));
    }

    [Fact]
    public async Task An_invoice_not_read_yet_is_a_conflict()
    {
        var invoice = await AddInvoiceAsync(read: false);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(Command(invoice.Id)));
    }

    [Fact]
    public async Task An_approved_invoice_is_a_conflict()
    {
        var invoice = await AddInvoiceAsync(approve: true);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(Command(invoice.Id)));
        Assert.Equal(12.1m, invoice.ExtractedData?.TotalAmount);
    }
}
