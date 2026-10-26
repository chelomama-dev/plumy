using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices.Extraction;

/// <summary>Reads an invoice's data from its file (Azure Document Intelligence; a fake one locally and in tests).</summary>
public interface IInvoiceExtractor
{
    /// <summary>
    /// Throws <see cref="InvoiceExtractionException"/> when the file can't be read and trying again won't help.
    /// Any other exception is taken as transient (the service is down, a timeout) and the message is retried.
    /// </summary>
    Task<InvoiceExtractionResult> ExtractAsync(
        Stream content, InvoiceFileFormat format, CancellationToken cancellationToken = default);
}

/// <summary>What was read, and how sure the reader was of each field (0 to 1). A field it didn't read has none.</summary>
public sealed record InvoiceExtractionResult(
    ExtractedInvoiceData Data, IReadOnlyDictionary<InvoiceField, double> Confidence)
{
    /// <summary>Data from a reader that gives no confidence.</summary>
    public InvoiceExtractionResult(ExtractedInvoiceData data) : this(data, new Dictionary<InvoiceField, double>())
    {
    }
}

/// <summary>
/// The file can't be read, and it never will be: retrying is pointless. The message is shown to the person
/// reviewing the invoice, so it must not carry technical details.
/// </summary>
public sealed class InvoiceExtractionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
