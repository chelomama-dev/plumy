using System.Net.Http.Headers;
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

    /// <summary>A valid token for the configured tenant and API, ready to tweak with <c>with</c>.</summary>
    public TokenSettings ValidToken()
    {
        var configuration = Services.GetRequiredService<IConfiguration>();
        return new TokenSettings(configuration["Entra:Authority"]!, configuration["Entra:Audience"]!);
    }

    /// <summary>A client that sends a valid access token on every request.</summary>
    public HttpClient CreateAuthenticatedClient() => CreateClient(TestTokens.Create(ValidToken()));

    public HttpClient CreateClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public Task InitializeAsync() => _container.StartAsync();

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
