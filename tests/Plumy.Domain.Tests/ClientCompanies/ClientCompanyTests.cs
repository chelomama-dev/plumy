using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Domain.Tests.ClientCompanies;

public class ClientCompanyTests
{
    private static readonly TaxId ValidTaxId = TaxId.Create("B12345674");

    [Fact]
    public void Create_WithValidData_SetsProperties()
    {
        var company = ClientCompany.Create("Acme S.L.", ValidTaxId);

        Assert.NotEqual(Guid.Empty, company.Id);
        Assert.Equal("Acme S.L.", company.Name);
        Assert.Equal(ValidTaxId, company.TaxId);
    }

    [Fact]
    public void Create_TrimsName()
    {
        var company = ClientCompany.Create("  Acme S.L.  ", ValidTaxId);

        Assert.Equal("Acme S.L.", company.Name);
    }

    [Fact]
    public void Create_GeneratesDifferentIds()
    {
        var a = ClientCompany.Create("Acme S.L.", ValidTaxId);
        var b = ClientCompany.Create("Acme S.L.", ValidTaxId);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_ThrowsDomainException(string? name)
    {
        Assert.Throws<DomainException>(() => ClientCompany.Create(name, ValidTaxId));
    }

    [Fact]
    public void Create_WithNameTooLong_ThrowsDomainException()
    {
        var name = new string('a', ClientCompany.MaxNameLength + 1);

        Assert.Throws<DomainException>(() => ClientCompany.Create(name, ValidTaxId));
    }

    [Fact]
    public void Create_WithNameAtMaxLength_Succeeds()
    {
        var name = new string('a', ClientCompany.MaxNameLength);

        var company = ClientCompany.Create(name, ValidTaxId);

        Assert.Equal(name, company.Name);
    }

    [Fact]
    public void Rename_WithValidName_ChangesName()
    {
        var company = ClientCompany.Create("Acme S.L.", ValidTaxId);

        company.Rename(" Acme Global S.A. ");

        Assert.Equal("Acme Global S.A.", company.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithEmptyName_ThrowsAndKeepsOldName(string? name)
    {
        var company = ClientCompany.Create("Acme S.L.", ValidTaxId);

        Assert.Throws<DomainException>(() => company.Rename(name));
        Assert.Equal("Acme S.L.", company.Name);
    }
}
