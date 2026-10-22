using System.Text;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class InvoiceFileFormatTests
{
    [Fact]
    public void Detect_Pdf()
    {
        Assert.Equal(InvoiceFileFormat.Pdf, InvoiceFileFormats.Detect("%PDF-1.7\n"u8));
    }

    [Fact]
    public void Detect_Jpeg()
    {
        Assert.Equal(InvoiceFileFormat.Jpeg, InvoiceFileFormats.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]));
    }

    [Fact]
    public void Detect_Png()
    {
        Assert.Equal(InvoiceFileFormat.Png,
            InvoiceFileFormats.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%PD")]
    [InlineData("hello, world")]
    [InlineData("<html><body>%PDF-</body></html>")]
    [InlineData("PK\u0003\u0004")] // zip, docx, xlsx...
    public void Detect_AnythingElse_ReturnsNull(string content)
    {
        Assert.Null(InvoiceFileFormats.Detect(Encoding.Latin1.GetBytes(content)));
    }

    [Theory]
    [InlineData(InvoiceFileFormat.Pdf, "application/pdf")]
    [InlineData(InvoiceFileFormat.Jpeg, "image/jpeg")]
    [InlineData(InvoiceFileFormat.Png, "image/png")]
    public void ContentType_IsTheStandardMediaType(InvoiceFileFormat format, string expected)
    {
        Assert.Equal(expected, format.ContentType());
    }
}
