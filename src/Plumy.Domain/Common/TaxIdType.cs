namespace Plumy.Domain.Common;

public enum TaxIdType
{
    /// <summary>Spanish citizens: 8 digits + check letter (12345678Z).</summary>
    Nif,

    /// <summary>Foreign residents: X/Y/Z + 7 digits + check letter (X1234567L).</summary>
    Nie,

    /// <summary>Legal entities: letter + 7 digits + check digit or letter (B12345674).</summary>
    Cif
}
