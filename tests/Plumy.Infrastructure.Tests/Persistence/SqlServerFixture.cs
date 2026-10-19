using Microsoft.EntityFrameworkCore;
using Plumy.Application.Abstractions;
using Plumy.Domain.Firms;
using Plumy.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Plumy.Infrastructure.Tests.Persistence;

/// <summary>
/// Starts one SQL Server container per test class, applies the real migrations and creates two firms.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Same image the AppHost uses, so tests run against the same engine as local dev
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>The firm <see cref="CreateDbContext"/> works for unless told otherwise.</summary>
    public Firm Firm { get; } = Firm.Create("Gestoría López", Guid.NewGuid());

    /// <summary>A second tenant, to check that one firm never sees the other's data.</summary>
    public Firm OtherFirm { get; } = Firm.Create("Asesoría Martínez", Guid.NewGuid());

    public PlumyDbContext CreateDbContext() => CreateDbContext(Firm.Id);

    public PlumyDbContext CreateDbContext(Guid firmId)
    {
        var options = new DbContextOptionsBuilder<PlumyDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        return new PlumyDbContext(options, new FixedTenant(firmId));
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.Firms.AddRange(Firm, OtherFirm);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class FixedTenant(Guid firmId) : ITenantContext
    {
        public Guid FirmId => firmId;
    }
}
