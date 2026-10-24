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
        invoice.CompleteExtraction(Data, Now);
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
        Assert.Throws<DomainException>(() => NewInvoice().CompleteExtraction(Data, Now));
        Assert.Throws<DomainException>(() => Extracted().CompleteExtraction(Data, Now));
        Assert.Throws<DomainException>(() => Failed().CompleteExtraction(Data, Now));
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

        invoice.RetryExtraction();

        Assert.Equal(InvoiceExtractionStatus.Pending, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractionError);
    }

    [Fact]
    public void RetryExtraction_FromPending_StaysPending()
    {
        // Queueing may have failed after the upload was saved
        var invoice = NewInvoice();

        invoice.RetryExtraction();

        Assert.Equal(InvoiceExtractionStatus.Pending, invoice.ExtractionStatus);
    }

    [Fact]
    public void RetryExtraction_WhenProcessingOrExtracted_Throws()
    {
        Assert.Throws<DomainException>(() => Processing().RetryExtraction());
        Assert.Throws<DomainException>(() => Extracted().RetryExtraction());
    }

    [Fact]
    public void AFailedInvoice_CanBeRetriedAndExtracted()
    {
        var invoice = Failed();

        invoice.RetryExtraction();
        invoice.StartExtraction();
        invoice.CompleteExtraction(Data, Now);

        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractionError);
    }
}
