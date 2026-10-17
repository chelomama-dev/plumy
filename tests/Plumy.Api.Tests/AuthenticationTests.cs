using System.Net;
using Microsoft.AspNetCore.Hosting;
using Plumy.Api.Tests.Infrastructure;

namespace Plumy.Api.Tests;

/// <summary>
/// Every endpoint requires a valid Entra access token for this API with the access_as_user scope
/// (fallback policy), so these tests use one protected endpoint as a sample.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthenticationTests(PlumyApiFactory factory)
{
    private const string ProtectedPath = "/api/client-companies";

    [Fact]
    public async Task Request_WithValidToken_Returns200()
    {
        var response = await factory.CreateAuthenticatedClient().GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithExpiredToken_Returns401()
    {
        // Beyond the default 5 minutes of clock skew
        var token = factory.ValidToken() with { Expires = DateTime.UtcNow.AddMinutes(-10) };

        var response = await SendWith(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithTokenForAnotherApi_Returns401()
    {
        // A token the same tenant issued for a different application must not open this API
        var token = factory.ValidToken() with { Audience = Guid.NewGuid().ToString() };

        var response = await SendWith(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithTokenFromAnotherTenant_Returns401()
    {
        var otherTenant = Guid.NewGuid();
        var token = factory.ValidToken() with
        {
            Issuer = $"https://{otherTenant}.ciamlogin.com/{otherTenant}/v2.0",
        };

        var response = await SendWith(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithTokenSignedByUntrustedKey_Returns401()
    {
        // Same claims as a valid token: only the signature gives it away
        var token = factory.ValidToken() with { Key = TestTokens.UntrustedKey };

        var response = await SendWith(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("User.Read")]
    public async Task Request_WithoutAccessAsUserScope_Returns403(string? scope)
    {
        // Authenticated (valid token) but not authorized: the app didn't ask for permission to call this API
        var token = factory.ValidToken() with { Scope = scope };

        var response = await SendWith(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithScopeAmongOthers_Returns200()
    {
        // scp is a space-separated list
        var token = factory.ValidToken() with { Scope = "openid access_as_user offline_access" };

        var response = await SendWith(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectedToken_InProduction_DoesNotExplainWhy()
    {
        // The reason ("issuer invalid", "signature invalid"...) helps an attacker tune a forged token
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"))
            .CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestTokens.Create(factory.ValidToken() with { Audience = Guid.NewGuid().ToString() }));

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Null(challenge.Parameter);
    }

    private Task<HttpResponseMessage> SendWith(TokenSettings token) =>
        factory.CreateClient(TestTokens.Create(token)).GetAsync(ProtectedPath);
}
