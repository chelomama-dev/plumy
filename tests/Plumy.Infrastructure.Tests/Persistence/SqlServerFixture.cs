using Microsoft.EntityFrameworkCore;
using Plumy.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Plumy.Infrastructure.Tests.Persistence;

/// <summary>
/// Starts one SQL Server container per test class and applies the real migrations.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Same image the AppHost uses, so tests run against the same engine as local dev
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public PlumyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlumyDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        return new PlumyDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
