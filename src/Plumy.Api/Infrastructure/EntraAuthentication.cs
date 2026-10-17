using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Plumy.Api.Infrastructure;

/// <summary>The "Entra" configuration section: identifiers of the External ID tenant and this API (not secrets).</summary>
internal sealed class EntraOptions
{
    public const string SectionName = "Entra";

    /// <summary>Token issuer, e.g. https://{tenant id}.ciamlogin.com/{tenant id}/v2.0.</summary>
    [Required, Url]
    public required string Authority { get; init; }

    /// <summary>The API's application (client) id: the <c>aud</c> of v2 access tokens.</summary>
    [Required]
    public required string Audience { get; init; }

    /// <summary>Delegated scope the SPA must request to call the API.</summary>
    [Required]
    public required string Scope { get; init; }
}

internal static class EntraAuthentication
{
    /// <summary>
    /// Validates Entra External ID access tokens and requires one, with the API's scope, on every endpoint
    /// unless it opts out with <c>AllowAnonymous</c> (health probes).
    /// </summary>
    public static IServiceCollection AddEntraAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var entra = configuration.GetSection(EntraOptions.SectionName).Get<EntraOptions>()
            ?? throw new InvalidOperationException($"Missing the '{EntraOptions.SectionName}' configuration section.");
        Validator.ValidateObject(entra, new ValidationContext(entra), validateAllProperties: true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Signing keys come from {Authority}/.well-known/openid-configuration and are cached and rotated
                options.Authority = entra.Authority;
                options.TokenValidationParameters.ValidIssuer = entra.Authority;
                options.TokenValidationParameters.ValidAudience = entra.Audience;

                // Keep the token's claim names (oid, scp, email) instead of the long WS-Federation URIs
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";

                // Why a token was rejected (bad issuer, signature...) goes in WWW-Authenticate only while developing:
                // in production it would help an attacker tune a forged token. The logs always have it.
                options.IncludeErrorDetails = environment.IsDevelopment();
            });

        var requireApiScope = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasScope(context.User, entra.Scope))
            .Build();

        services.AddAuthorizationBuilder()
            // [Authorize] / RequireAuthorization() without a policy
            .SetDefaultPolicy(requireApiScope)
            // Endpoints with no authorization metadata at all: secure by default
            .SetFallbackPolicy(requireApiScope);

        return services;
    }

    // scp is a space-separated list, e.g. "openid access_as_user"
    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindFirst("scp")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope) ?? false;
}
