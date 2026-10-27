using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class InvoiceExtractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly ExtractedInvoiceData Data = ExtractedInvoiceData.Create(supplierName: "Papelería Sol");

    private static Invoice NewInvoice() =>
        Invoice.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "factura.pdf", InvoiceFileFormat.Pdf, 1000,
            new string('a', 64), Guid.NewGuid(), Now);

    private static Invoice Processing()
    {
        var invoice = NewInvoice();
        invoice.StartExtraction();
        return invoice;
    }

    private static Invoice Failed()
    {
        var invoice = Processing();
        invoice.FailExtraction("Unreadable.");
        return invoice;
    }

    private static Invoice Extracted()
    {
        var invoice = Processing();
        invoice.CompleteExtraction(Data, InvoiceValidation.None, Now);
        return invoice;
    }

    [Fact]
    public void Create_StartsPendingWithNoData()
    {
        var invoice = NewInvoice();

        Assert.Equal(InvoiceExtractionStatus.Pending, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractedData);
        Assert.Null(invoice.ExtractionError);
        Assert.Null(invoice.ExtractedAt);
        Assert.NotEqual(Guid.Empty, invoice.ExtractionAttemptId);
        Assert.Equal(Now, invoice.ExtractionQueuedAt);
    }

    [Fact]
    public void StartExtraction_FromPending_IsProcessing()
    {
        Assert.Equal(InvoiceExtractionStatus.Processing, Processing().ExtractionStatus);
    }

    [Fact]
    public void StartExtraction_WhenAlreadyProcessing_IsAccepted()
    {
        // A redelivered message after a worker crashed mid-way
        var invoice = Processing();

        invoice.StartExtraction();

        Assert.Equal(InvoiceExtractionStatus.Processing, invoice.ExtractionStatus);
    }

    [Fact]
    public void StartExtraction_WhenExtractedOrFailed_Throws()
    {
        Assert.Throws<DomainException>(() => Extracted().StartExtraction());
        Assert.Throws<DomainException>(() => Failed().StartExtraction());
    }

    [Fact]
    public void CompleteExtraction_StoresDataAndDate()
    {
        var invoice = Extracted();

        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Equal(Data, invoice.ExtractedData);
        Assert.Equal(Now, invoice.ExtractedAt);
    }

    [Fact]
    public void CompleteExtraction_WhenNotProcessing_Throws()
    {
        Assert.Throws<DomainException>(() => NewInvoice().CompleteExtraction(Data, InvoiceValidation.None, Now));
        Assert.Throws<DomainException>(() => Extracted().CompleteExtraction(Data, InvoiceValidation.None, Now));
        Assert.Throws<DomainException>(() => Failed().CompleteExtraction(Data, InvoiceValidation.None, Now));
    }

    [Fact]
    public void FailExtraction_StoresTrimmedReason()
    {
        var invoice = Processing();

        invoice.FailExtraction("  The file is not an invoice.  ");

        Assert.Equal(InvoiceExtractionStatus.Failed, invoice.ExtractionStatus);
        Assert.Equal("The file is not an invoice.", invoice.ExtractionError);
        Assert.Null(invoice.ExtractedData);
    }

    [Fact]
    public void FailExtraction_ShortensALongReason()
    {
        var invoice = Processing();

        invoice.FailExtraction(new string('x', Invoice.MaxExtractionErrorLength + 50));

        Assert.Equal(Invoice.MaxExtractionErrorLength, invoice.ExtractionError!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FailExtraction_WithoutReason_Throws(string reason)
    {
        Assert.Throws<DomainException>(() => Processing().FailExtraction(reason));
    }

    [Fact]
    public void FailExtraction_WhenNotProcessing_Throws()
    {
        Assert.Throws<DomainException>(() => NewInvoice().FailExtraction("x"));
        Assert.Throws<DomainException>(() => Extracted().FailExtraction("x"));
    }

    [Fact]
    public void RetryExtraction_FromFailed_IsPendingAndClearsTheError()
    {
        var invoice = Failed();

        invoice.RetryExtraction(Now.AddSeconds(10));

        Assert.Equal(InvoiceExtractionStatus.Pending, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractionError);
    }

    [Fact]
    public void RetryExtraction_IsANewAttempt_QueuedNow()
    {
        var invoice = Failed();
        var firstAttempt = invoice.ExtractionAttemptId;

        invoice.RetryExtraction(Now.AddMinutes(1));

        Assert.NotEqual(firstAttempt, invoice.ExtractionAttemptId);
        Assert.Equal(Now.AddMinutes(1), invoice.ExtractionQueuedAt);
    }

    [Fact]
    public void RetryExtraction_FromPending_OnceStuck_StaysPending()
    {
        // Queueing may have failed after the upload was saved
        var invoice = NewInvoice();

        invoice.RetryExtraction(Now + Invoice.StuckPendingAfter);

        Assert.Equal(InvoiceExtractionStatus.Pending, invoice.ExtractionStatus);
    }

    [Fact]
    public void RetryExtraction_FromPending_TooSoon_Throws()
    {
        // Its message is probably still on its way: a second one would read the invoice twice
        var invoice = NewInvoice();
        var attempt = invoice.ExtractionAttemptId;

        Assert.Throws<DomainException>(() => invoice.RetryExtraction(Now + Invoice.StuckPendingAfter - TimeSpan.FromSeconds(1)));
        Assert.Equal(attempt, invoice.ExtractionAttemptId);
    }

    [Fact]
    public void RetryExtraction_Twice_WaitsAgainFromTheRetry()
    {
        // The wait counts from the latest queueing, not from the upload: the case that read an invoice twice
        var invoice = NewInvoice();
        var retriedAt = Now.AddHours(1);
        invoice.RetryExtraction(retriedAt);

        Assert.Throws<DomainException>(() => invoice.RetryExtraction(retriedAt.AddMinutes(1)));
        invoice.RetryExtraction(retriedAt + Invoice.StuckPendingAfter);
    }

    [Fact]
    public void IsStuckInQueue_OnlyWhenPendingForLongEnough()
    {
        Assert.False(NewInvoice().IsStuckInQueue(Now.AddMinutes(1)));
        Assert.True(NewInvoice().IsStuckInQueue(Now + Invoice.StuckPendingAfter));
        Assert.False(Processing().IsStuckInQueue(Now.AddHours(1)));
        Assert.False(Failed().IsStuckInQueue(Now.AddHours(1)));
    }

    [Fact]
    public void RetryExtraction_WhenProcessingOrExtracted_Throws()
    {
        Assert.Throws<DomainException>(() => Processing().RetryExtraction(Now.AddHours(1)));
        Assert.Throws<DomainException>(() => Extracted().RetryExtraction(Now.AddHours(1)));
    }

    [Fact]
    public void AFailedInvoice_CanBeRetriedAndExtracted()
    {
        var invoice = Failed();

        invoice.RetryExtraction(Now);
        invoice.StartExtraction();
        invoice.CompleteExtraction(Data, InvoiceValidation.None, Now);

        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractionError);
    }

    [Fact]
    public void CompleteExtraction_KeepsWhatTheChecksFound()
    {
        var invoice = Processing();
        var earlier = Guid.NewGuid();

        invoice.CompleteExtraction(Data, new InvoiceValidation(
            [InvoiceIssue.AmountsMismatch, InvoiceIssue.PossibleDuplicate, InvoiceIssue.AmountsMismatch], earlier), Now);

        Assert.Equal([InvoiceIssue.AmountsMismatch, InvoiceIssue.PossibleDuplicate], invoice.Issues);
        Assert.Equal(earlier, invoice.DuplicateOfInvoiceId);
    }

    [Fact]
    public void NewInvoice_HasNoIssues()
    {
        Assert.Empty(NewInvoice().Issues);
        Assert.Null(NewInvoice().DuplicateOfInvoiceId);
    }

    [Fact]
    public void CompleteExtraction_AsADuplicateOfItself_Throws()
    {
        var invoice = Processing();

        Assert.Throws<DomainException>(() =>
            invoice.CompleteExtraction(Data, new InvoiceValidation([InvoiceIssue.PossibleDuplicate], invoice.Id), Now));
    }
}
