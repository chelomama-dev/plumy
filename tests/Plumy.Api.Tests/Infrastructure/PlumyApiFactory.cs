using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Plumy.Infrastructure;
using Testcontainers.MsSql;

namespace Plumy.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API in memory (same Program.cs, DI and middleware) against a throwaway SQL Server container.
/// </summary>
public sealed class PlumyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Replaces the connection string the AppHost injects when running under Aspire.
        // The environment stays Development, so Program.cs applies the real migrations on startup.
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.DatabaseName}", _container.GetConnectionString());

        // Trust the tests' signing key instead of downloading the tenant's keys (no network, no real users).
        // Only the keys change: issuer, audience, lifetime and scope are checked with the real configuration.
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = options.Authority };
                configuration.SigningKeys.Add(TestTokens.SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
    }

    // The user behind CreateAuthenticatedClient. Their firm is created once, when the factory starts
    private readonly Guid _defaultUserObjectId = Guid.NewGuid();

    /// <summary>A valid token for the configured tenant and API, ready to tweak with <c>with</c>.</summary>
    public TokenSettings ValidToken()
    {
        var configuration = Services.GetRequiredService<IConfiguration>();
        return new TokenSettings(configuration["Entra:Authority"]!, configuration["Entra:Audience"]!)
        {
            ObjectId = _defaultUserObjectId,
        };
    }

    /// <summary>A client for a user who belongs to a firm, shared by every test that just needs one.</summary>
    public HttpClient CreateAuthenticatedClient() => CreateClient(TestTokens.Create(ValidToken()));

    /// <summary>A client for a signed-in user who hasn't created or joined a firm yet.</summary>
    public HttpClient CreateClientWithoutFirm() =>
        CreateClient(TestTokens.Create(ValidToken() with { ObjectId = Guid.NewGuid() }));

    /// <summary>A client for a new user who owns a brand-new firm: a tenant no other test shares.</summary>
    public async Task<HttpClient> CreateClientInNewFirmAsync(string firmName = "Gestoría de pruebas")
    {
        var client = CreateClientWithoutFirm();
        var response = await client.PostAsJsonAsync("/api/firms", new { name = firmName });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public HttpClient CreateClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var response = await CreateClient(TestTokens.Create(ValidToken()))
            .PostAsJsonAsync("/api/firms", new { name = "Gestoría por defecto" });
        response.EnsureSuccessStatusCode();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Shares one API + container across every test class in the collection: starting SQL Server takes seconds.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PlumyApiFactory>
{
    public const string Name = "Api";
}
