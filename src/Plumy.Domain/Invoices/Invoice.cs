using System.Text.RegularExpressions;
using Plumy.Domain.Common;

namespace Plumy.Domain.Invoices;

/// <summary>
/// A supplier invoice received by a client company, uploaded as a file. Its data is read from the file in the
/// background: <see cref="ExtractionStatus"/> says how far that has gone.
/// </summary>
public sealed partial class Invoice
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 255;
    public const int Sha256Length = 64;
    public const int MaxStoragePathLength = 120;
    public const int MaxExtractionErrorLength = 500;

    public Guid Id { get; private set; }

    /// <summary>The accounting firm (the tenant). Redundant with the company's, but every tenant filter needs it.</summary>
    public Guid FirmId { get; private set; }

    public Guid ClientCompanyId { get; private set; }

    /// <summary>The name the file had on the uploader's machine. Only for display: never used as a path.</summary>
    public string FileName { get; private set; }

    public InvoiceFileFormat Format { get; private set; }
    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 of the content (lowercase hex): the same file uploaded twice has the same hash.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Where the file lives in storage. Built from ids only, so user input never reaches a path.</summary>
    public string StoragePath { get; private set; }

    /// <summary>Entra object id (<c>oid</c>) of who uploaded it.</summary>
    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public InvoiceExtractionStatus ExtractionStatus { get; private set; } = InvoiceExtractionStatus.Pending;

    /// <summary>What was read from the file. Only set once <see cref="ExtractionStatus"/> is Extracted.</summary>
    public ExtractedInvoiceData? ExtractedData { get; private set; }

    /// <summary>Why the last extraction failed, written for the person reviewing it. Only set when Failed.</summary>
    public string? ExtractionError { get; private set; }

    public DateTimeOffset? ExtractedAt { get; private set; }

    private Invoice(
        Guid id, Guid firmId, Guid clientCompanyId, string fileName, InvoiceFileFormat format, long sizeBytes,
        string sha256, Guid uploadedBy, DateTimeOffset uploadedAt)
    {
        Id = id;
        FirmId = firmId;
        ClientCompanyId = clientCompanyId;
        FileName = fileName;
        Format = format;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StoragePath = $"{firmId}/{clientCompanyId}/{id}";
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
    }

    public static Invoice Create(
        Guid firmId, Guid clientCompanyId, string? fileName, InvoiceFileFormat format, long sizeBytes,
        string? sha256, Guid uploadedBy, DateTimeOffset uploadedAt)
    {
        if (firmId == Guid.Empty)
            throw new DomainException("The invoice must belong to a firm.");
        if (clientCompanyId == Guid.Empty)
            throw new DomainException("The invoice must belong to a client company.");
        if (uploadedBy == Guid.Empty)
            throw new DomainException("The invoice must have an uploader.");
        if (!Enum.IsDefined(format))
            throw new DomainException("Unknown file format.");
        if (sha256 is null || !Sha256Pattern().IsMatch(sha256))
            throw new DomainException("The file hash must be a lowercase hex SHA-256.");

        EnsureValidSize(sizeBytes);

        return new Invoice(
            Guid.CreateVersion7(), firmId, clientCompanyId, NormalizeFileName(fileName), format, sizeBytes, sha256,
            uploadedBy, uploadedAt);
    }

    /// <summary>
    /// The worker takes the invoice. Also valid when it is already Processing: messages are delivered at least once,
    /// and a worker that crashed mid-way leaves it there for the redelivery to pick up.
    /// </summary>
    public void StartExtraction()
    {
        if (ExtractionStatus is not (InvoiceExtractionStatus.Pending or InvoiceExtractionStatus.Processing))
            throw new DomainException($"An invoice in status {ExtractionStatus} can't start an extraction.");

        ExtractionStatus = InvoiceExtractionStatus.Processing;
    }

    public void CompleteExtraction(ExtractedInvoiceData data, DateTimeOffset extractedAt)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureProcessing();

        ExtractionStatus = InvoiceExtractionStatus.Extracted;
        ExtractedData = data;
        ExtractionError = null;
        ExtractedAt = extractedAt;
    }

    public void FailExtraction(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A failed extraction needs a reason.");
        EnsureProcessing();

        var trimmed = reason.Trim();
        ExtractionStatus = InvoiceExtractionStatus.Failed;
        ExtractionError = trimmed.Length > MaxExtractionErrorLength ? trimmed[..MaxExtractionErrorLength] : trimmed;
    }

    /// <summary>
    /// Puts the invoice back in the queue's hands. From Failed, to try again; from Pending, because queueing it may
    /// have failed after the upload was saved. Never from Processing (a worker has it) or Extracted (it's done).
    /// </summary>
    public void RetryExtraction()
    {
        if (ExtractionStatus is not (InvoiceExtractionStatus.Pending or InvoiceExtractionStatus.Failed))
            throw new DomainException($"An invoice in status {ExtractionStatus} can't be retried.");

        ExtractionStatus = InvoiceExtractionStatus.Pending;
        ExtractionError = null;
    }

    private void EnsureProcessing()
    {
        if (ExtractionStatus != InvoiceExtractionStatus.Processing)
            throw new DomainException($"The invoice is {ExtractionStatus}, not being processed.");
    }

    /// <summary>Public so the upload can reject a file before reading it.</summary>
    public static void EnsureValidSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
            throw new DomainException("The file is empty.");
        if (sizeBytes > MaxFileSizeBytes)
            throw new DomainException($"The file cannot exceed {MaxFileSizeBytes / (1024 * 1024)} MB.");
    }

    // Keeps what a person would recognise: no folders (some browsers send the full path), no control characters.
    // A name that is too long is shortened, keeping its extension, instead of rejecting the upload
    private static string NormalizeFileName(string? fileName)
    {
        var name = fileName ?? "";
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (name.Length == 0)
            throw new DomainException("The file name is required.");

        if (name.Length <= MaxFileNameLength)
            return name;

        var extension = Path.GetExtension(name);
        if (extension.Length >= MaxFileNameLength / 2)
            extension = "";

        return name[..(MaxFileNameLength - extension.Length)] + extension;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}
