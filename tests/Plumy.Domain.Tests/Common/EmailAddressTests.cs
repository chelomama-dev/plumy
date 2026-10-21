using Plumy.Domain.Common;

namespace Plumy.Domain.Tests.Common;

public class EmailAddressTests
{
    [Theory]
    [InlineData("ana@gestorialopez.es")]
    [InlineData("ana.garcia+plumy@gmail.com")]
    [InlineData("a@b.co")]
    public void Create_WithValidAddress_KeepsIt(string value)
    {
        Assert.Equal(value, EmailAddress.Create(value).Value);
    }

    [Fact]
    public void Create_TrimsAndLowercases()
    {
        Assert.Equal("ana@gestorialopez.es", EmailAddress.Create("  Ana@GestoriaLopez.ES ").Value);
    }

    [Fact]
    public void TwoSpellingsOfTheSameAddress_AreEqual()
    {
        Assert.Equal(EmailAddress.Create("ANA@gestoria.es"), EmailAddress.Create("ana@Gestoria.es"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutValue_Throws(string? value)
    {
        Assert.Throws<DomainException>(() => EmailAddress.Create(value));
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("@gestoria.es")]
    [InlineData("ana@@gestoria.es")]
    [InlineData("ana garcia@gestoria.es")]
    [InlineData("Ana <ana@gestoria.es>")]
    [InlineData("ana@localhost")]
    public void Create_WithInvalidAddress_Throws(string value)
    {
        Assert.Throws<DomainException>(() => EmailAddress.Create(value));
    }

    [Fact]
    public void Create_AtMaxLength_Succeeds()
    {
        var value = AddressOfLength(EmailAddress.MaxLength);

        Assert.Equal(value, EmailAddress.Create(value).Value);
    }

    [Fact]
    public void Create_LongerThanSmtpAllows_Throws()
    {
        Assert.Throws<DomainException>(() => EmailAddress.Create(AddressOfLength(EmailAddress.MaxLength + 1)));
    }

    // 60 chars @ labels of 60 chars, padded to the exact length: valid in every other respect
    private static string AddressOfLength(int length)
    {
        var address = new string('a', 60) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 3)) + ".";
        address += new string('c', length - address.Length - 3) + ".es";
        Assert.Equal(length, address.Length);
        return address;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not an email")]
    public void TryCreate_WithMissingOrInvalidValue_ReturnsFalse(string? value)
    {
        Assert.False(EmailAddress.TryCreate(value, out var email));
        Assert.Null(email);
    }

    [Fact]
    public void TryCreate_WithValidValue_Normalizes()
    {
        Assert.True(EmailAddress.TryCreate("Ana@Gestoria.es", out var email));
        Assert.Equal("ana@gestoria.es", email.Value);
    }
}
