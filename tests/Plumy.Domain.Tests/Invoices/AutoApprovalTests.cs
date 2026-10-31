using Plumy.Domain.Common;
using Plumy.Domain.Firms;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class AutoApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly AutoApprovalPolicy On = AutoApprovalPolicy.Create(true, 0.95);

    // Everything a booked line needs, read with no issue
    private static readonly ExtractedInvoiceData Clean = ExtractedInvoiceData.Create(
        supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: "F-42", issueDate: new DateOnly(2026, 10, 1),
        netAmount: 100m, taxAmount: 21m, totalAmount: 121m, currency: "EUR");

    private static Dictionary<InvoiceField, double> Sure(double confidence = 0.99) =>
        AutoApprovalPolicy.BookedFields.ToDictionary(field => field, _ => confidence);

    private static Invoice Read(
        ExtractedInvoiceData? data = null, IReadOnlyDictionary<InvoiceField, double>? confidence = null,
        IReadOnlyList<InvoiceIssue>? issues = null, Guid? duplicateOf = null)
    {
        var invoice = Invoice.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "factura.pdf", InvoiceFileFormat.Pdf,
            1000, new string('a', 64), Guid.NewGuid(), Now);
        invoice.StartExtraction();
        invoice.CompleteExtraction(
            data ?? Clean, new InvoiceValidation(issues ?? [], duplicateOf), Now, confidence ?? Sure());
        return invoice;
    }

    private static Invoice Suggested(
        LedgerAccountSuggestionSource source = LedgerAccountSuggestionSource.SupplierHistory,
        IReadOnlyDictionary<InvoiceField, double>? confidence = null, IReadOnlyList<InvoiceIssue>? issues = null)
    {
        var invoice = Read(confidence: confidence, issues: issues);
        invoice.SuggestLedgerAccount(LedgerAccountSuggestion.Create("628", source, null, Now));
        return invoice;
    }

    [Fact]
    public void A_new_firm_has_it_off()
    {
        var firm = Firm.Create("Gestoría López", Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.False(firm.AutoApproval.Enabled);
        Assert.Equal(AutoApprovalPolicy.DefaultMinConfidence, firm.AutoApproval.MinConfidence);
    }

    [Fact]
    public void The_owner_can_turn_it_on()
    {
        var firm = Firm.Create("Gestoría López", Guid.NewGuid(), DateTimeOffset.UtcNow);

        firm.SetAutoApproval(AutoApprovalPolicy.Create(true, 0.9));

        Assert.Equal(AutoApprovalPolicy.Create(true, 0.9), firm.AutoApproval);
    }

    [Theory]
    [InlineData(0.79)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void The_minimum_confidence_must_be_between_0_8_and_1(double minConfidence)
    {
        Assert.Throws<DomainException>(() => AutoApprovalPolicy.Create(true, minConfidence));
    }

    [Fact]
    public void A_clean_invoice_with_the_suppliers_usual_account_is_approved()
    {
        var invoice = Suggested();

        invoice.ApproveAutomatically(On, Now);

        Assert.Equal(InvoiceReviewStatus.Approved, invoice.ReviewStatus);
        Assert.True(invoice.ApprovedAutomatically);
        Assert.Null(invoice.ReviewedBy);
        Assert.Equal(Now, invoice.ReviewedAt);
        Assert.Equal("628", invoice.LedgerAccountCode);
        // Not a person's choice: it mustn't teach the suggestions
        Assert.Null(invoice.LedgerAccountConfirmedBy);
    }

    [Fact]
    public void Not_while_the_firm_has_it_off()
    {
        Assert.False(Suggested().CanBeApprovedAutomatically(AutoApprovalPolicy.Disabled));
    }

    [Fact]
    public void Never_with_the_language_models_account()
    {
        Assert.False(Suggested(LedgerAccountSuggestionSource.Ai).CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Never_without_a_suggestion()
    {
        Assert.False(Read().CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Never_with_an_issue()
    {
        Assert.False(Suggested(issues: [InvoiceIssue.UnusualTaxAmount]).CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Never_for_a_possible_duplicate()
    {
        var invoice = Read(duplicateOf: Guid.CreateVersion7());
        invoice.SuggestLedgerAccount(
            LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.SupplierHistory, null, Now));

        Assert.False(invoice.CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Not_when_a_booked_field_is_below_the_line()
    {
        var confidence = Sure();
        confidence[InvoiceField.TotalAmount] = 0.94;

        Assert.False(Suggested(confidence: confidence).CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Exactly_on_the_line_is_enough()
    {
        Assert.True(Suggested(confidence: Sure(0.95)).CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void A_doubtful_field_that_is_not_booked_doesnt_block_it()
    {
        var confidence = Sure();
        confidence[InvoiceField.SupplierName] = 0.3;

        Assert.True(Suggested(confidence: confidence).CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Not_when_a_booked_field_has_no_confidence()
    {
        var confidence = Sure();
        confidence.Remove(InvoiceField.TaxAmount);

        Assert.False(Suggested(confidence: confidence).CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Not_when_a_booked_field_wasnt_read()
    {
        // No VAT read: no issue says so, but there is nothing to book
        var invoice = Read(ExtractedInvoiceData.Create(
            supplierTaxId: "A81948077", invoiceNumber: "F-42", issueDate: new DateOnly(2026, 10, 1), netAmount: 100m,
            totalAmount: 121m));
        invoice.SuggestLedgerAccount(
            LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.SupplierHistory, null, Now));

        Assert.False(invoice.CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Not_once_a_person_has_touched_it()
    {
        var corrected = Suggested();
        corrected.CorrectData(ExtractedInvoiceData.Create(
            supplierName: "Endesa", supplierTaxId: "A81948077", invoiceNumber: "F-43",
            issueDate: new DateOnly(2026, 10, 1), netAmount: 100m, taxAmount: 21m, totalAmount: 121m, currency: "EUR"),
            new InvoiceValidation([], null));

        var withAccount = Suggested();
        withAccount.ConfirmLedgerAccount("629", Guid.NewGuid(), Now);

        Assert.False(corrected.CanBeApprovedAutomatically(On));
        Assert.False(withAccount.CanBeApprovedAutomatically(On));
    }

    [Fact]
    public void Approving_one_that_doesnt_qualify_throws()
    {
        var invoice = Suggested(LedgerAccountSuggestionSource.Ai);

        Assert.Throws<DomainException>(() => invoice.ApproveAutomatically(On, Now));
        Assert.Equal(InvoiceReviewStatus.Unreviewed, invoice.ReviewStatus);
    }

    [Fact]
    public void Reopening_makes_it_a_persons_again()
    {
        var invoice = Suggested();
        invoice.ApproveAutomatically(On, Now);

        invoice.Reopen();

        Assert.Equal(InvoiceReviewStatus.Unreviewed, invoice.ReviewStatus);
        Assert.False(invoice.ApprovedAutomatically);
        Assert.Equal("628", invoice.LedgerAccountCode);
    }

    [Fact]
    public void A_person_approving_it_again_owns_its_account()
    {
        var reviewer = Guid.NewGuid();
        var invoice = Suggested();
        invoice.ApproveAutomatically(On, Now);
        invoice.Reopen();

        invoice.Approve(reviewer, Now.AddHours(1));

        Assert.False(invoice.ApprovedAutomatically);
        Assert.Equal(reviewer, invoice.ReviewedBy);
        Assert.Equal(reviewer, invoice.LedgerAccountConfirmedBy);
        Assert.Equal(Now.AddHours(1), invoice.LedgerAccountConfirmedAt);
    }

    [Fact]
    public void A_person_approving_keeps_who_chose_the_account()
    {
        var chooser = Guid.NewGuid();
        var invoice = Suggested();
        invoice.ConfirmLedgerAccount("628", chooser, Now);

        invoice.Approve(Guid.NewGuid(), Now.AddHours(1));

        Assert.Equal(chooser, invoice.LedgerAccountConfirmedBy);
        Assert.Equal(Now, invoice.LedgerAccountConfirmedAt);
    }
}
