using System.Text.RegularExpressions;

namespace Plumy.Domain.Common;

/// <summary>
/// Spanish tax identification number (NIF, NIE or CIF). Always valid once created.
/// </summary>
public sealed partial record TaxId
{
    private const string NifLetters = "TRWAGMYFPDXBNJZSQVHLCKE";
    private const string NiePrefixes = "XYZ";
    private const string CifControlLetters = "JABCDEFGHI";

    public string Value { get; }
    public TaxIdType Type { get; }

    private TaxId(string value, TaxIdType type)
    {
        Value = value;
        Type = type;
    }

    public static TaxId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Tax ID is required.");

        var normalized = Normalize(value);

        if (NifPattern().IsMatch(normalized) && HasValidNifLetter(normalized))
            return new TaxId(normalized, TaxIdType.Nif);

        if (NiePattern().IsMatch(normalized) && HasValidNieLetter(normalized))
            return new TaxId(normalized, TaxIdType.Nie);

        if (CifPattern().IsMatch(normalized) && HasValidCifControl(normalized))
            return new TaxId(normalized, TaxIdType.Cif);

        throw new DomainException($"'{value}' is not a valid Spanish tax ID.");
    }

    public override string ToString() => Value;

    private static string Normalize(string value) =>
        SeparatorPattern().Replace(value, string.Empty).ToUpperInvariant();

    private static bool HasValidNifLetter(string nif)
    {
        var number = int.Parse(nif.AsSpan(0, 8));
        return nif[8] == NifLetters[number % 23];
    }

    // A NIE is a NIF where the leading X/Y/Z stands for 0/1/2
    private static bool HasValidNieLetter(string nie)
    {
        var nif = NiePrefixes.IndexOf(nie[0]) + nie[1..];
        return HasValidNifLetter(nif);
    }

    private static bool HasValidCifControl(string cif)
    {
        var sum = 0;
        for (var i = 1; i <= 7; i++)
        {
            var digit = cif[i] - '0';

            // Odd positions (1, 3, 5, 7) are doubled and their digits added: 7 → 14 → 1 + 4 = 5
            if (i % 2 == 1)
            {
                digit *= 2;
                digit = digit / 10 + digit % 10;
            }

            sum += digit;
        }

        var control = (10 - sum % 10) % 10;
        var isDigitMatch = cif[8] == (char)('0' + control);
        var isLetterMatch = cif[8] == CifControlLetters[control];

        return cif[0] switch
        {
            'A' or 'B' or 'E' or 'H' => isDigitMatch,
            'N' or 'P' or 'Q' or 'R' or 'S' or 'W' => isLetterMatch,
            _ => isDigitMatch || isLetterMatch
        };
    }

    [GeneratedRegex(@"[\s.\-]")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"^\d{8}[A-Z]$")]
    private static partial Regex NifPattern();

    [GeneratedRegex(@"^[XYZ]\d{7}[A-Z]$")]
    private static partial Regex NiePattern();

    [GeneratedRegex(@"^[ABCDEFGHJNPQRSUVW]\d{7}[0-9A-J]$")]
    private static partial Regex CifPattern();
}
