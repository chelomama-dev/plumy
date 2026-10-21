using Plumy.Domain.Common;

namespace Plumy.Domain.Firms;

/// <summary>
/// How a user appears to their team: a copy of the name and email of their Entra account, as the access token
/// had them the last time they signed in. Informational only: users are identified by their object id.
/// </summary>
public sealed record UserProfile
{
    public const int MaxNameLength = 200;

    public static readonly UserProfile Empty = new(null, null);

    public string? Name { get; }
    public EmailAddress? Email { get; }

    public UserProfile(string? name, EmailAddress? email)
    {
        // Comes from the identity provider, not from a form: never reject a sign-in over it, just tidy it up
        var trimmed = name?.Trim();
        Name = string.IsNullOrEmpty(trimmed) ? null
            : trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength]
            : trimmed;
        Email = email;
    }

    /// <summary>From the raw claims of a token; an email claim that isn't a valid address is ignored.</summary>
    public static UserProfile FromClaims(string? name, string? email) =>
        new(name, EmailAddress.TryCreate(email, out var parsed) ? parsed : null);
}
