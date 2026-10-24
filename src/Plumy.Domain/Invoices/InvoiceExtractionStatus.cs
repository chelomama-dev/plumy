namespace Plumy.Domain.Invoices;

/// <summary>
/// Where an invoice is in the extraction of its data. Extraction runs in the background (the worker), so the
/// upload only leaves it <see cref="Pending"/>.
/// </summary>
public enum InvoiceExtractionStatus
{
    /// <summary>Uploaded and waiting for the worker (or for a retry to be queued).</summary>
    Pending,

    /// <summary>The worker has taken it. Stays here between attempts while a transient error is retried.</summary>
    Processing,

    /// <summary>The data was read. Final, until a later version lets a person correct it.</summary>
    Extracted,

    /// <summary>It could not be read. A person can ask for a retry, which takes it back to <see cref="Pending"/>.</summary>
    Failed
}
