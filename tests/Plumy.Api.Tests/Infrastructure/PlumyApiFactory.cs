using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
