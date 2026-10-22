using Plumy.Domain.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Domain.Tests.Invoices;

public class InvoiceTests
{
    private static readonly Guid FirmId = Guid.CreateVersion7();
    private static readonly Guid CompanyId = Guid.CreateVersion7();
    private static readonly Guid UploaderId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    private static Invoice Create(
        string? fileName = "factura.pdf", long sizeBytes = 1000, string? sha256 = null, Guid? firmId = null,
        Guid? companyId = null, Guid? uploadedBy = null, InvoiceFileFormat format = InvoiceFileFormat.Pdf) =>
        Invoice.Create(firmId ?? FirmId, companyId ?? CompanyId, fileName, format, sizeBytes, sha256 ?? Hash,
            uploadedBy ?? UploaderId, Now);

    [Fact]
    public void Create_WithValidData_SetsProperties()
    {
        var invoice = Create();

        Assert.NotEqual(Guid.Empty, invoice.Id);
        Assert.Equal(FirmId, invoice.FirmId);
        Assert.Equal(CompanyId, invoice.ClientCompanyId);
        Assert.Equal("factura.pdf", invoice.FileName);
        Assert.Equal(InvoiceFileFormat.Pdf, invoice.Format);
        Assert.Equal(1000, invoice.SizeBytes);
        Assert.Equal(Hash, invoice.Sha256);
        Assert.Equal(UploaderId, invoice.UploadedBy);
        Assert.Equal(Now, invoice.UploadedAt);
    }

    [Fact]
    public void Create_StoragePathIsBuiltFromIdsOnly()
    {
        var invoice = Create(fileName: "../../etc/passwd.pdf");

        Assert.Equal($"{FirmId}/{CompanyId}/{invoice.Id}", invoice.StoragePath);
        Assert.True(invoice.StoragePath.Length <= Invoice.MaxStoragePathLength);
    }

    [Theory]
    [InlineData("C:\\Users\\ana\\Facturas\\factura.pdf", "factura.pdf")]
    [InlineData("/home/ana/factura.pdf", "factura.pdf")]
    [InlineData("  factura.pdf  ", "factura.pdf")]
    [InlineData("fac\ttura\u0000.pdf", "factura.pdf")]
    [InlineData("Factura Ñandú 2026.pdf", "Factura Ñandú 2026.pdf")]
    public void Create_NormalizesFileName(string fileName, string expected)
    {
        Assert.Equal(expected, Create(fileName: fileName).FileName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("carpeta/")]
    public void Create_WithoutFileName_Throws(string? fileName)
    {
        Assert.Throws<DomainException>(() => Create(fileName: fileName));
    }

    [Fact]
    public void Create_WithLongFileName_ShortensItKeepingTheExtension()
    {
        var invoice = Create(fileName: new string('x', 300) + ".pdf");

        Assert.Equal(Invoice.MaxFileNameLength, invoice.FileName.Length);
        Assert.EndsWith("x.pdf", invoice.FileName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Invoice.MaxFileSizeBytes + 1)]
    public void Create_WithInvalidSize_Throws(long sizeBytes)
    {
        Assert.Throws<DomainException>(() => Create(sizeBytes: sizeBytes));
    }

    [Fact]
    public void Create_WithMaxSize_IsAccepted()
    {
        Assert.Equal(Invoice.MaxFileSizeBytes, Create(sizeBytes: Invoice.MaxFileSizeBytes).SizeBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Create_WithInvalidHash_Throws(string sha256)
    {
        Assert.Throws<DomainException>(() => Create(sha256: sha256));
    }

    [Fact]
    public void Create_WithoutFirmCompanyOrUploader_Throws()
    {
        Assert.Throws<DomainException>(() => Create(firmId: Guid.Empty));
        Assert.Throws<DomainException>(() => Create(companyId: Guid.Empty));
        Assert.Throws<DomainException>(() => Create(uploadedBy: Guid.Empty));
    }

    [Fact]
    public void Create_WithUnknownFormat_Throws()
    {
        Assert.Throws<DomainException>(() => Create(format: (InvoiceFileFormat)42));
    }
}
