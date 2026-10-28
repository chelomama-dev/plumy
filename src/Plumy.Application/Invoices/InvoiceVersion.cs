using Plumy.Application.Common;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Invoices;

/// <summary>
/// The invoice's version as the API shows it (hex, like "00000000000007D1"): in each response, and in the ETag. A
/// client sends it back in If-Match when it changes the invoice.
/// </summary>
public static class InvoiceVersion
{
    public static string Of(Invoice invoice) => Convert.ToHexString(invoice.Version);

    /// <summary>
    /// Throws when the client read an older version than the one stored: someone else (or the worker) changed it
    /// since. Nothing expected (no If-Match) means the client takes the risk: there is nothing to compare.
    /// </summary>
    public static void EnsureMatches(Invoice invoice, string? expected)
    {
        if (expected is not null && !string.Equals(expected, Of(invoice), StringComparison.OrdinalIgnoreCase))
            throw new PreconditionFailedException(
                "The invoice changed since you loaded it. Load it again to see the changes.");
    }
}
