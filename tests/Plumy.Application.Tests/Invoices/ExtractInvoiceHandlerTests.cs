using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Invoices.ExtractInvoice;
using Plumy.Application.Invoices.Extraction;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Invoices;

public class ExtractInvoiceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInvoiceStore _invoices = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly FakeExtractor _extractor = new();
    private readonly FakeClassifier _classifier = new();
    private readonly ExtractInvoiceHandler _handler;

    public ExtractInvoiceHandlerTests()
    {
        var clock = new FixedTimeProvider(Now);
        _handler = new ExtractInvoiceHandler(
            _invoices, _storage, _extractor, new LedgerAccountSuggester(_invoices, _classifier, clock), _invoices,
            clock, NullLogger<ExtractInvoiceHandler>.Instance);
    }

    private async Task<Invoice> AddInvoiceAsync(byte[]? content = null, bool storeFile = true)
    {
        var invoice = Invoice.Create(TestTenant.FirmId, Guid.CreateVersion7(), "factura.png", InvoiceFileFormat.Png,
            1000, new string('a', 64), Guid.NewGuid(), Now.AddMinutes(-5));
        if (storeFile)
            await _storage.UploadAsync(invoice.StoragePath, new MemoryStream(content ?? TestFiles.Png()), "image/png");

        _invoices.Add(invoice);
        await _invoices.SaveChangesAsync();
        return invoice;
    }

    private Task<ExtractInvoiceOutcome> HandleAsync(Guid invoiceId, bool isLastAttempt = false) =>
        _handler.HandleAsync(new ExtractInvoiceCommand(invoiceId, isLastAttempt));

    [Fact]
    public async Task HandleAsync_ReadsTheFileAndStoresTheData()
    {
        var content = TestFiles.Png();
        var invoice = await AddInvoiceAsync(content);

        var outcome = await HandleAsync(invoice.Id);

        Assert.Equal(ExtractInvoiceOutcome.Extracted, outcome);
        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Equal(_extractor.Result, invoice.ExtractedData);
        Assert.Equal(Now, invoice.ExtractedAt);

        var call = Assert.Single(_extractor.Calls);
        Assert.Equal(content, call.Content);
        Assert.Equal(InvoiceFileFormat.Png, call.Format);
    }

    [Fact]
    public async Task HandleAsync_SavesProcessingBeforeReading()
    {
        var invoice = await AddInvoiceAsync();
        var savesBeforeRead = -1;
        InvoiceExtractionStatus? statusDuringRead = null;
        _extractor.OnExtract = () =>
        {
            savesBeforeRead = _invoices.SaveCount;
            statusDuringRead = invoice.ExtractionStatus;
        };
        var savesAtStart = _invoices.SaveCount;

        await HandleAsync(invoice.Id);

        Assert.Equal(InvoiceExtractionStatus.Processing, statusDuringRead);
        Assert.Equal(savesAtStart + 1, savesBeforeRead);
        // Then the data, then the suggestion
        Assert.Equal(savesAtStart + 3, _invoices.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_UnknownInvoice_IsNotFound()
    {
        Assert.Equal(ExtractInvoiceOutcome.NotFound, await HandleAsync(Guid.NewGuid()));
        Assert.Empty(_extractor.Calls);
    }

    [Fact]
    public async Task HandleAsync_SameMessageTwice_ReadsOnce()
    {
        // At-least-once delivery: the second copy finds the invoice finished
        var invoice = await AddInvoiceAsync();
        await HandleAsync(invoice.Id);

        var outcome = await HandleAsync(invoice.Id);

        Assert.Equal(ExtractInvoiceOutcome.AlreadyFinished, outcome);
        Assert.Single(_extractor.Calls);
    }

    [Fact]
    public async Task HandleAsync_AfterAFailure_DoesNotReadAgain()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new InvoiceExtractionException("Not an invoice.");
        await HandleAsync(invoice.Id);
        _extractor.FailWith = null;

        Assert.Equal(ExtractInvoiceOutcome.AlreadyFinished, await HandleAsync(invoice.Id));
        Assert.Equal(InvoiceExtractionStatus.Failed, invoice.ExtractionStatus);
    }

    [Fact]
    public async Task HandleAsync_RedeliveryWhileProcessing_ReadsAgain()
    {
        // The worker crashed after saving Processing: the redelivered message finishes the job
        var invoice = await AddInvoiceAsync();
        invoice.StartExtraction();
        await _invoices.SaveChangesAsync();

        Assert.Equal(ExtractInvoiceOutcome.Extracted, await HandleAsync(invoice.Id));
    }

    [Fact]
    public async Task HandleAsync_UnreadableFile_FailsWithTheExtractorsMessage()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new InvoiceExtractionException("The file is not an invoice.");

        var outcome = await HandleAsync(invoice.Id);

        Assert.Equal(ExtractInvoiceOutcome.Failed, outcome);
        Assert.Equal(InvoiceExtractionStatus.Failed, invoice.ExtractionStatus);
        Assert.Equal("The file is not an invoice.", invoice.ExtractionError);
    }

    [Fact]
    public async Task HandleAsync_MissingFile_Fails()
    {
        var invoice = await AddInvoiceAsync(storeFile: false);

        var outcome = await HandleAsync(invoice.Id);

        Assert.Equal(ExtractInvoiceOutcome.Failed, outcome);
        Assert.Equal(ExtractInvoiceHandler.MissingFileError, invoice.ExtractionError);
        Assert.Empty(_extractor.Calls);
    }

    [Fact]
    public async Task HandleAsync_TransientError_ThrowsSoTheMessageIsRetried()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new HttpRequestException("503");

        await Assert.ThrowsAsync<HttpRequestException>(() => HandleAsync(invoice.Id));

        // Left for the next delivery, which StartExtraction accepts
        Assert.Equal(InvoiceExtractionStatus.Processing, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractionError);
    }

    [Fact]
    public async Task HandleAsync_TransientErrorOnTheLastAttempt_Fails()
    {
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new HttpRequestException("503 with internal details");

        var outcome = await HandleAsync(invoice.Id, isLastAttempt: true);

        Assert.Equal(ExtractInvoiceOutcome.Failed, outcome);
        Assert.Equal(InvoiceExtractionStatus.Failed, invoice.ExtractionStatus);
        // The person reviewing it never sees the technical error
        Assert.Equal(ExtractInvoiceHandler.GaveUpError, invoice.ExtractionError);
    }

    [Fact]
    public async Task HandleAsync_CancelledOnTheLastAttempt_IsNotAFailure()
    {
        // The worker is shutting down: the message goes back to the queue, the invoice isn't to blame
        var invoice = await AddInvoiceAsync();
        _extractor.FailWith = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => HandleAsync(invoice.Id, isLastAttempt: true));

        Assert.Equal(InvoiceExtractionStatus.Processing, invoice.ExtractionStatus);
    }

    [Fact]
    public async Task HandleAsync_SuggestsTheAccountOnceRead()
    {
        var invoice = await AddInvoiceAsync();

        await HandleAsync(invoice.Id);

        var suggestion = invoice.LedgerAccountSuggestion;
        Assert.NotNull(suggestion);
        Assert.Equal("628", suggestion.Code);
        Assert.Equal(LedgerAccountSuggestionSource.Ai, suggestion.Source);
        Assert.Equal(Now, suggestion.SuggestedAt);
        Assert.Equal(_extractor.Result, Assert.Single(_classifier.Requests).Invoice);
    }

    [Fact]
    public async Task HandleAsync_NothingToSuggest_StillExtracted()
    {
        var invoice = await AddInvoiceAsync();
        _classifier.Result = null;

        Assert.Equal(ExtractInvoiceOutcome.Extracted, await HandleAsync(invoice.Id));

        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Null(invoice.LedgerAccountSuggestion);
    }

    [Fact]
    public async Task HandleAsync_SuggestionFailsTransiently_RetriesOnlyTheSuggestion()
    {
        var invoice = await AddInvoiceAsync();
        _classifier.FailWith = new HttpRequestException("429");

        await Assert.ThrowsAsync<HttpRequestException>(() => HandleAsync(invoice.Id));

        // The data is kept: the retry doesn't pay for reading the file again
        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Null(invoice.LedgerAccountSuggestion);

        _classifier.FailWith = null;
        Assert.Equal(ExtractInvoiceOutcome.Extracted, await HandleAsync(invoice.Id));

        Assert.Single(_extractor.Calls);
        Assert.Equal("628", invoice.LedgerAccountSuggestion?.Code);
    }

    [Fact]
    public async Task HandleAsync_SuggestionFailsOnTheLastAttempt_LeavesItWithoutOne()
    {
        var invoice = await AddInvoiceAsync();
        _classifier.FailWith = new HttpRequestException("503");

        var outcome = await HandleAsync(invoice.Id, isLastAttempt: true);

        // Read is read: a person can still pick the account
        Assert.Equal(ExtractInvoiceOutcome.Extracted, outcome);
        Assert.Equal(InvoiceExtractionStatus.Extracted, invoice.ExtractionStatus);
        Assert.Null(invoice.ExtractionError);
        Assert.Null(invoice.LedgerAccountSuggestion);
    }

    [Fact]
    public async Task HandleAsync_AlreadySuggested_DoesNotAskAgain()
    {
        var invoice = await AddInvoiceAsync();
        await HandleAsync(invoice.Id);

        Assert.Equal(ExtractInvoiceOutcome.AlreadyFinished, await HandleAsync(invoice.Id));
        Assert.Single(_classifier.Requests);
    }

    [Fact]
    public async Task HandleAsync_AccountAlreadyConfirmed_DoesNotSuggest()
    {
        // The suggestion failed, and a person picked the account before the retry came
        var invoice = await AddInvoiceAsync();
        _classifier.FailWith = new HttpRequestException("503");
        await HandleAsync(invoice.Id, isLastAttempt: true);
        invoice.ConfirmLedgerAccount("629", Guid.NewGuid(), Now);
        _classifier.FailWith = null;

        Assert.Equal(ExtractInvoiceOutcome.AlreadyFinished, await HandleAsync(invoice.Id));
        Assert.Null(invoice.LedgerAccountSuggestion);
    }
}
