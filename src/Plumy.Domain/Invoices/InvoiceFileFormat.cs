namespace Plumy.Domain.Invoices;

/// <summary>The file types an invoice can be uploaded as.</summary>
public enum InvoiceFileFormat
{
    Pdf,
    Jpeg,
    Png
}

public static class InvoiceFileFormats
{
    /// <summary>How many leading bytes <see cref="Detect"/> needs to recognise every format.</summary>
    public const int SignatureLength = 8;

    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Recognises the format from the file's first bytes (its "magic number"). The name and the Content-Type come
    /// from the client and can say anything; the content can't lie about what it is.
    /// </summary>
    public static InvoiceFileFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PdfSignature))
            return InvoiceFileFormat.Pdf;
        if (header.StartsWith(JpegSignature))
            return InvoiceFileFormat.Jpeg;
        if (header.StartsWith(PngSignature))
            return InvoiceFileFormat.Png;

        return null;
    }

    public static string ContentType(this InvoiceFileFormat format) => format switch
    {
        InvoiceFileFormat.Pdf => "application/pdf",
        InvoiceFileFormat.Jpeg => "image/jpeg",
        InvoiceFileFormat.Png => "image/png",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };
}
