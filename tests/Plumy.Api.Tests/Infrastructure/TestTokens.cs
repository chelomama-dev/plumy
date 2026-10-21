using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Plumy.Api.Tests.Infrastructure;

/// <summary>
/// Issues access tokens shaped like Entra External ID's (v2, scp, oid, email), signed with a key that only the
/// tests know. <see cref="PlumyApiFactory"/> makes the API trust that key instead of the tenant's public keys;
/// issuer, audience and lifetime are still validated with the API's real configuration.
/// </summary>
public static class TestTokens
{
    public static readonly SecurityKey SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "plumy-tests" };

    /// <summary>A key the API doesn't trust, to check that the signature is validated.</summary>
    public static readonly SecurityKey UntrustedKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "untrusted" };

    public static string Create(TokenSettings settings)
    {
        var claims = new Dictionary<string, object>
        {
            ["oid"] = settings.ObjectId.ToString(),
            ["sub"] = settings.ObjectId.ToString(),
            ["name"] = settings.Name,
        };
        if (settings.Email is not null)
        {
            claims["email"] = settings.Email;
        }
        if (settings.Scope is not null)
        {
            claims["scp"] = settings.Scope;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Claims = claims,
            IssuedAt = settings.Expires.AddHours(-1),
            NotBefore = settings.Expires.AddHours(-1),
            Expires = settings.Expires,
            SigningCredentials = new SigningCredentials(settings.Key, SecurityAlgorithms.RsaSha256),
        });
    }
}

/// <summary>A valid token by default; each test changes the one thing it wants to break.</summary>
public sealed record TokenSettings(string Issuer, string Audience)
{
    public string? Scope { get; init; } = "access_as_user";
    public DateTime Expires { get; init; } = DateTime.UtcNow.AddHours(1);
    public SecurityKey Key { get; init; } = TestTokens.SigningKey;
    public Guid ObjectId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Test User";
    public string? Email { get; init; } = "test.user@example.com";
}
