namespace Plumy.Api.Tests.Infrastructure;

/// <summary>
/// Generates valid, unique NIFs so tests sharing the database never collide on the unique index.
/// </summary>
internal static class TaxIds
{
    private const string NifLetters = "TRWAGMYFPDXBNJZSQVHLCKE";

    public static string NewNif()
    {
        var number = Random.Shared.Next(0, 100_000_000);
        return $"{number:D8}{NifLetters[number % 23]}";
    }
}
