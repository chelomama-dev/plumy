using System.Text.RegularExpressions;
using Plumy.Domain.Common;

namespace Plumy.Domain.Invoices;

/// <summary>
/// A supplier invoice received by a client company, uploaded as a file. For now it only knows about the file;
/// the data extracted from it comes in later versions.
/// </summary>
public sealed partial class Invoice
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 255;
    public const int Sha256Length = 64;
    public const int MaxStoragePathLength = 120;

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
