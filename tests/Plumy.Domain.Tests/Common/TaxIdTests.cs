using Plumy.Domain.Common;

namespace Plumy.Domain.Tests.Common;

public class TaxIdTests
{
    // NIF: 8 digits + letter. Letter = "TRWAGMYFPDXBNJZSQVHLCKE"[number % 23]
    [Theory]
    [InlineData("12345678Z")]
    [InlineData("00000000T")]
    [InlineData("87654321X")]
    public void Create_WithValidNif_ReturnsNif(string value)
    {
        var taxId = TaxId.Create(value);

        Assert.Equal(value, taxId.Value);
        Assert.Equal(TaxIdType.Nif, taxId.Type);
    }

    // NIE: X/Y/Z are replaced by 0/1/2 and validated like a NIF
    [Theory]
    [InlineData("X1234567L")]
    [InlineData("Y1234567X")]
    [InlineData("Z1234567R")]
    public void Create_WithValidNie_ReturnsNie(string value)
    {
        var taxId = TaxId.Create(value);

        Assert.Equal(value, taxId.Value);
        Assert.Equal(TaxIdType.Nie, taxId.Type);
    }

    // CIF: letter + 7 digits + control. A, B, E, H require a digit; N, P, Q, R, S, W require a letter;
    // the rest accept either.
    [Theory]
    [InlineData("B12345674")]
    [InlineData("B87654323")]
    [InlineData("A00000000")]
    [InlineData("Q1234567D")]
    [InlineData("G12345674")]
    [InlineData("G1234567D")]
    public void Create_WithValidCif_ReturnsCif(string value)
    {
        var taxId = TaxId.Create(value);

        Assert.Equal(value, taxId.Value);
        Assert.Equal(TaxIdType.Cif, taxId.Type);
    }

    [Theory]
    [InlineData("12345678z", "12345678Z")]
    [InlineData(" 12345678Z ", "12345678Z")]
    [InlineData("12.345.678-Z", "12345678Z")]
    [InlineData("b-1234567-4", "B12345674")]
    [InlineData("x 1234567 l", "X1234567L")]
    public void Create_NormalizesInput(string value, string expected)
    {
        var taxId = TaxId.Create(value);

        Assert.Equal(expected, taxId.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("patata")]
    [InlineData("12345678")]   // missing letter
    [InlineData("1234567Z")]   // too short
    [InlineData("123456789Z")] // too long
    [InlineData("12345678A")]  // wrong NIF letter
    [InlineData("X1234567A")]  // wrong NIE letter
    [InlineData("B12345675")]  // wrong CIF digit
    [InlineData("B1234567D")]  // B requires a digit, not a letter
    [InlineData("Q12345674")]  // Q requires a letter, not a digit
    [InlineData("I12345674")]  // I is not a valid CIF letter
    public void Create_WithInvalidValue_ThrowsDomainException(string? value)
    {
        Assert.Throws<DomainException>(() => TaxId.Create(value));
    }

    [Fact]
    public void TaxIds_WithSameValue_AreEqual()
    {
        var a = TaxId.Create("12345678Z");
        var b = TaxId.Create("12345678-z");

        Assert.Equal(a, b);
    }
}
