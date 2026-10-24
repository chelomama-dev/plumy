using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging;
using Plumy.Application.Invoices.Extraction;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Extraction;

/// <summary>
/// Reads invoices with Azure Document Intelligence's prebuilt invoice model (v4.0, API 2024-11-30). The client
/// signs in with the worker's managed identity: the account has no keys.
/// </summary>
internal sealed partial class DocumentIntelligenceInvoiceExtractor(
    DocumentIntelligenceClient client,
    ILogger<DocumentIntelligenceInvoiceExtractor> logger) : IInvoiceExtractor
{
    internal const string ModelId = "prebuilt-invoice";

    // A hint, not a filter: it tells the model how to read dates (03/04 is 3 April) and numbers (1.234,56)
    internal const string Locale = "es-ES";

    public const string UnreadableError = "The file could not be read. Check that it is a valid, unprotected PDF or image.";
    public const string NoInvoiceError = "No invoice was found in the file.";

    public async Task<ExtractedInvoiceData> ExtractAsync(
        Stream content, InvoiceFileFormat format, CancellationToken cancellationToken = default)
    {
        // The service takes the bytes in the request body. Uploads are at most 10 MB; the free tier (F0) refuses
        // anything over 4 MB, which ends here as an unreadable file
        var bytes = await BinaryData.FromStreamAsync(content, cancellationToken);
        var options = new AnalyzeDocumentOptions(ModelId, bytes) { Locale = Locale };

        AnalyzeResult result;
        try
        {
            // Started and polled until it's done (a few seconds): the worker has nothing else to do meanwhile
            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
            result = operation.Value;
        }
        catch (RequestFailedException ex) when (IsUnreadable(ex))
        {
            throw new InvoiceExtractionException(UnreadableError, ex);
        }

        // One document per invoice. A file with several (a scan of three invoices) is read as its first one; the
        // reviewer will see the others are missing
        if (result.Documents is not [var document, ..])
            throw new InvoiceExtractionException(NoInvoiceError);

        if (result.Documents.Count > 1)
            LogSeveralDocuments(result.Documents.Count);

        return InvoiceFieldMapper.Map(document.Fields);
    }

    // The service refused the file itself (corrupt, password protected, too big, not a document): sending the same
    // bytes again gives the same answer. Throttling (429), outages (5xx) and auth problems are worth retrying, and
    // the SDK already retried them a few times before this point
    private static bool IsUnreadable(RequestFailedException ex) =>
        ex.Status is 400 or 413 or 415
        // A failed analysis comes back from the polling request, whose own status is 200
        || ex.ErrorCode is "InvalidRequest" or "InvalidContent" or "InvalidContentLength" or "UnsupportedContent";

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file holds {Count} documents; only the first is read")]
    private partial void LogSeveralDocuments(int count);
}
