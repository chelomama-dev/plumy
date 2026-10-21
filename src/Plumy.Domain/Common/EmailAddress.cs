using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

namespace Plumy.Domain.Common;

/// <summary>
/// An email address, trimmed and lowercased so that two spellings of the same address compare equal.
/// Always valid once created.
/// </summary>
public sealed record EmailAddress
{
    /// <summary>The longest address SMTP allows (RFC 5321).</summary>
    public const int MaxLength = 254;

    public string Value { get; }

    private EmailAddress(string value) => Value = value;

    public static EmailAddress Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Email address is required.");

        return Parse(value) ?? throw new DomainException($"'{value.Trim()}' is not a valid email address.");
    }

    // For values that come from outside and may be missing or odd, like the email claim of a token
    public static bool TryCreate(string? value, [NotNullWhen(true)] out EmailAddress? email)
    {
        email = string.IsNullOrWhiteSpace(value) ? null : Parse(value);
        return email is not null;
    }

    public override string ToString() => Value;

    private static EmailAddress? Parse(string value)
    {
        // The domain part is case-insensitive and, in practice, so is the local part: every provider ignores it
        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
            return null;

        // MailAddress also accepts display names ("Ana <ana@x.es>"); requiring the parsed address to be the
        // whole input keeps only bare addresses. The dot in the domain rules out local hosts like "ana@localhost"
        return MailAddress.TryCreate(normalized, out var parsed)
               && parsed.Address == normalized
               && parsed.Host.Contains('.')
            ? new EmailAddress(normalized)
            : null;
    }
}
