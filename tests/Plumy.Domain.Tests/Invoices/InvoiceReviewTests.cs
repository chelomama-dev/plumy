using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class InvoiceReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static readonly ExtractedInvoiceData Read = ExtractedInvoiceData.Create(
        supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: "F-42", totalAmount: 121m);

    private static Invoice NewInvoice() =>
        Invoice.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "factura.pdf", InvoiceFileFormat.Pdf, 1000,
            new string('a', 64), Guid.NewGuid(), Now);

    private static Invoice Extracted(IReadOnlyDictionary<InvoiceField, double>? confidence = null)
    {
        var invoice = NewInvoice();
        invoice.StartExtraction();
        invoice.CompleteExtraction(Read, new InvoiceValidation([InvoiceIssue.AmountsMismatch], null), Now, confidence);
        return invoice;
    }

    private static Invoice Failed()
    {
        var invoice = NewInvoice();
        invoice.StartExtraction();
        invoice.FailExtraction("Unreadable.");
        return invoice;
    }

    private static Invoice WithAccount()
    {
        var invoice = Extracted();
        invoice.ConfirmLedgerAccount("628", Reviewer, Now);
        return invoice;
    }

    private static Invoice Approved()
    {
        var invoice = WithAccount();
        invoice.Approve(Reviewer, Now);
        return invoice;
    }

    [Fact]
    public void A_new_invoice_is_unreviewed()
    {
        var invoice = NewInvoice();

        Assert.Equal(InvoiceReviewStatus.Unreviewed, invoice.ReviewStatus);
        Assert.Empty(invoice.FieldConfidence);
        Assert.Empty(invoice.CorrectedFields);
    }

    [Fact]
    public void Completing_the_extraction_keeps_the_confidence()
    {
        var invoice = Extracted(new Dictionary<InvoiceField, double> { [InvoiceField.TotalAmount] = 0.42 });

        Assert.Equal(0.42, invoice.FieldConfidence[InvoiceField.TotalAmount]);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void A_confidence_out_of_range_is_rejected(double value)
    {
        var invoice = NewInvoice();
        invoice.StartExtraction();

        Assert.Throws<DomainException>(() => invoice.CompleteExtraction(Read, InvoiceValidation.None, Now,
            new Dictionary<InvoiceField, double> { [InvoiceField.TotalAmount] = value }));
    }

    [Fact]
    public void Correcting_replaces_the_data_marks_what_changed_and_applies_the_new_checks()
    {
        var invoice = Extracted();
        var duplicateOf = Guid.NewGuid();
        var fixedData = ExtractedInvoiceData.Create(
            supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: "F-43", totalAmount: 121.5m);

        invoice.CorrectData(fixedData, new InvoiceValidation([InvoiceIssue.PossibleDuplicate], duplicateOf));

        Assert.Equal(fixedData, invoice.ExtractedData);
        Assert.Equal([InvoiceField.InvoiceNumber, InvoiceField.TotalAmount], invoice.CorrectedFields);
        Assert.Equal([InvoiceIssue.PossibleDuplicate], invoice.Issues);
        Assert.Equal(duplicateOf, invoice.DuplicateOfInvoiceId);
    }

    [Fact]
    public void Corrections_add_up_and_a_field_stays_corrected()
    {
        var invoice = Extracted();

        invoice.CorrectData(ExtractedInvoiceData.Create(supplierName: "Endesa X", supplierTaxId: "A81948077",
            invoiceNumber: "F-42", totalAmount: 121m), InvoiceValidation.None);
        // Back to what was read, plus another change: the name was still touched by a person
        invoice.CorrectData(ExtractedInvoiceData.Create(supplierName: "Endesa", supplierTaxId: "A81948077",
            invoiceNumber: "F-42", totalAmount: 100m), InvoiceValidation.None);

        Assert.Equal([InvoiceField.SupplierName, InvoiceField.TotalAmount], invoice.CorrectedFields);
    }

    [Fact]
    public void Only_a_read_invoice_can_be_corrected()
    {
        Assert.Throws<DomainException>(() => NewInvoice().CorrectData(Read, InvoiceValidation.None));
        Assert.Throws<DomainException>(() => Failed().CorrectData(Read, InvoiceValidation.None));
    }

    [Fact]
    public void A_correction_cant_make_the_invoice_a_duplicate_of_itself()
    {
        var invoice = Extracted();

        Assert.Throws<DomainException>(() =>
            invoice.CorrectData(Read, new InvoiceValidation([InvoiceIssue.PossibleDuplicate], invoice.Id)));
    }

    [Fact]
    public void Approving_records_who_and_when()
    {
        var invoice = Approved();

        Assert.Equal(InvoiceReviewStatus.Approved, invoice.ReviewStatus);
        Assert.Equal(Reviewer, invoice.ReviewedBy);
        Assert.Equal(Now, invoice.ReviewedAt);
        Assert.Null(invoice.RejectionReason);
    }

    [Fact]
    public void Approving_needs_a_confirmed_account_not_a_suggestion()
    {
        var invoice = Extracted();
        invoice.SuggestLedgerAccount(LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.Ai, null, Now));

        Assert.Throws<DomainException>(() => invoice.Approve(Reviewer, Now));
        Assert.Equal(InvoiceReviewStatus.Unreviewed, invoice.ReviewStatus);
    }

    [Fact]
    public void Issues_dont_block_the_approval()
    {
        var invoice = WithAccount();
        Assert.NotEmpty(invoice.Issues);

        invoice.Approve(Reviewer, Now);

        Assert.Equal(InvoiceReviewStatus.Approved, invoice.ReviewStatus);
    }

    [Fact]
    public void An_approved_invoice_is_locked_until_reopened()
    {
        var invoice = Approved();

        Assert.Throws<DomainException>(() => invoice.CorrectData(Read, InvoiceValidation.None));
        Assert.Throws<DomainException>(() => invoice.ConfirmLedgerAccount("622", Reviewer, Now));
        Assert.Throws<DomainException>(() => invoice.Approve(Reviewer, Now));
        Assert.Throws<DomainException>(() => invoice.Reject("No", Reviewer, Now));

        invoice.Reopen();
        invoice.ConfirmLedgerAccount("622", Reviewer, Now);

        Assert.Equal("622", invoice.LedgerAccountCode);
    }

    [Fact]
    public void Rejecting_keeps_the_trimmed_reason()
    {
        var invoice = Extracted();

        invoice.Reject("  No es una factura  ", Reviewer, Now);

        Assert.Equal(InvoiceReviewStatus.Rejected, invoice.ReviewStatus);
        Assert.Equal("No es una factura", invoice.RejectionReason);
        Assert.Equal(Reviewer, invoice.ReviewedBy);
    }

    [Fact]
    public void An_unreadable_invoice_can_be_rejected_but_not_retried_then()
    {
        var invoice = Failed();

        invoice.Reject("No es una factura", Reviewer, Now);

        Assert.Equal(InvoiceReviewStatus.Rejected, invoice.ReviewStatus);
        Assert.Throws<DomainException>(invoice.RetryExtraction);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void A_rejection_needs_a_reason(string? reason)
    {
        Assert.Throws<DomainException>(() => Extracted().Reject(reason, Reviewer, Now));
    }

    [Fact]
    public void A_reason_too_long_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            Extracted().Reject(new string('x', Invoice.MaxRejectionReasonLength + 1), Reviewer, Now));
    }

    [Fact]
    public void An_invoice_still_being_read_cant_be_reviewed()
    {
        var invoice = NewInvoice();

        Assert.Throws<DomainException>(() => invoice.Reject("No", Reviewer, Now));
        Assert.Throws<DomainException>(() => invoice.Approve(Reviewer, Now));
    }

    [Fact]
    public void A_review_needs_someone()
    {
        Assert.Throws<DomainException>(() => WithAccount().Approve(Guid.Empty, Now));
    }

    [Fact]
    public void Reopening_forgets_the_decision()
    {
        var invoice = Extracted();
        invoice.Reject("Duplicada", Reviewer, Now);

        invoice.Reopen();

        Assert.Equal(InvoiceReviewStatus.Unreviewed, invoice.ReviewStatus);
        Assert.Null(invoice.ReviewedBy);
        Assert.Null(invoice.ReviewedAt);
        Assert.Null(invoice.RejectionReason);
    }

    [Fact]
    public void An_unreviewed_invoice_cant_be_reopened()
    {
        Assert.Throws<DomainException>(() => Extracted().Reopen());
    }
}
