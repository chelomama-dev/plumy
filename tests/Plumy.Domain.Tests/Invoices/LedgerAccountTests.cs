using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class LedgerAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.NewGuid();

    private static LedgerAccountSuggestion Suggestion(string code = "628") =>
        LedgerAccountSuggestion.Create(code, LedgerAccountSuggestionSource.Ai, "Electricidad.", Now);

    private static Invoice Pending() =>
        Invoice.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "factura.pdf", InvoiceFileFormat.Pdf, 1000,
            new string('a', 64), Guid.NewGuid(), Now);

    private static Invoice Extracted()
    {
        var invoice = Pending();
        invoice.StartExtraction();
        invoice.CompleteExtraction(ExtractedInvoiceData.Create(supplierName: "Endesa"), InvoiceValidation.None, Now);
        return invoice;
    }

    [Fact]
    public void Suggestion_KeepsItsValues()
    {
        var suggestion = LedgerAccountSuggestion.Create(
            "628", LedgerAccountSuggestionSource.SupplierHistory, null, Now);

        Assert.Equal("628", suggestion.Code);
        Assert.Equal(LedgerAccountSuggestionSource.SupplierHistory, suggestion.Source);
        Assert.Null(suggestion.Reason);
        Assert.Equal(Now, suggestion.SuggestedAt);
    }

    [Fact]
    public void Suggestion_OutsideTheCatalog_IsRejected()
    {
        Assert.Throws<DomainException>(() => Suggestion("999"));
    }

    [Theory]
    [InlineData("  ", null)]
    [InlineData("  Luz de la oficina.  ", "Luz de la oficina.")]
    public void Suggestion_TidiesTheReason(string reason, string? expected)
    {
        var suggestion = LedgerAccountSuggestion.Create("628", LedgerAccountSuggestionSource.Ai, reason, Now);

        Assert.Equal(expected, suggestion.Reason);
    }

    [Fact]
    public void Suggestion_ShortensALongReason()
    {
        var suggestion = LedgerAccountSuggestion.Create(
            "628", LedgerAccountSuggestionSource.Ai, new string('a', 1000), Now);

        Assert.Equal(LedgerAccountSuggestion.MaxReasonLength, suggestion.Reason?.Length);
    }

    [Fact]
    public void AReadInvoice_NeedsASuggestion()
    {
        Assert.False(Pending().NeedsLedgerAccountSuggestion);
        Assert.True(Extracted().NeedsLedgerAccountSuggestion);
    }

    [Fact]
    public void SuggestLedgerAccount_StoresIt()
    {
        var invoice = Extracted();
        var suggestion = Suggestion();

        invoice.SuggestLedgerAccount(suggestion);

        Assert.Equal(suggestion, invoice.LedgerAccountSuggestion);
        Assert.Null(invoice.LedgerAccountCode);
        Assert.False(invoice.NeedsLedgerAccountSuggestion);
    }

    [Fact]
    public void SuggestLedgerAccount_BeforeItIsRead_Throws()
    {
        Assert.Throws<DomainException>(() => Pending().SuggestLedgerAccount(Suggestion()));
    }

    [Fact]
    public void SuggestLedgerAccount_AfterAPersonChose_Throws()
    {
        var invoice = Extracted();
        invoice.ConfirmLedgerAccount("629", Person, Now);

        Assert.Throws<DomainException>(() => invoice.SuggestLedgerAccount(Suggestion()));
    }

    [Fact]
    public void ConfirmLedgerAccount_KeepsWhoAndWhenAndTheSuggestion()
    {
        var invoice = Extracted();
        invoice.SuggestLedgerAccount(Suggestion("628"));

        invoice.ConfirmLedgerAccount("622", Person, Now.AddHours(1));

        Assert.Equal("622", invoice.LedgerAccountCode);
        Assert.Equal(Person, invoice.LedgerAccountConfirmedBy);
        Assert.Equal(Now.AddHours(1), invoice.LedgerAccountConfirmedAt);
        // Kept, to measure how often the suggestions are right
        Assert.Equal("628", invoice.LedgerAccountSuggestion?.Code);
        Assert.False(invoice.NeedsLedgerAccountSuggestion);
    }

    [Fact]
    public void ConfirmLedgerAccount_CanChangeItsMind()
    {
        var invoice = Extracted();
        invoice.ConfirmLedgerAccount("622", Person, Now);

        invoice.ConfirmLedgerAccount("628", Person, Now.AddDays(1));

        Assert.Equal("628", invoice.LedgerAccountCode);
        Assert.Equal(Now.AddDays(1), invoice.LedgerAccountConfirmedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("999")]
    public void ConfirmLedgerAccount_OutsideTheCatalog_Throws(string? code)
    {
        Assert.Throws<DomainException>(() => Extracted().ConfirmLedgerAccount(code, Person, Now));
    }

    [Fact]
    public void ConfirmLedgerAccount_ByNobody_Throws()
    {
        Assert.Throws<DomainException>(() => Extracted().ConfirmLedgerAccount("628", Guid.Empty, Now));
    }

    [Fact]
    public void ConfirmLedgerAccount_BeforeItIsRead_Throws()
    {
        Assert.Throws<DomainException>(() => Pending().ConfirmLedgerAccount("628", Person, Now));
    }
}
