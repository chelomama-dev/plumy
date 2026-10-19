using Microsoft.EntityFrameworkCore;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Infrastructure.Persistence;

namespace Plumy.Infrastructure.Tests.Persistence;

public class ClientCompanyPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task SavedClientCompany_IsReadBackWithSameValues()
    {
        var company = ClientCompany.Create(fixture.Firm.Id, "Acme S.L.", TaxId.Create("B12345674"));

        await using (var db = fixture.CreateDbContext())
        {
            db.ClientCompanies.Add(company);
            await db.SaveChangesAsync();
        }

        // A fresh context forces a real round trip to the database instead of the change tracker
        await using (var db = fixture.CreateDbContext())
        {
            var loaded = await db.ClientCompanies.SingleAsync(c => c.Id == company.Id);

            Assert.Equal("Acme S.L.", loaded.Name);
            Assert.Equal(company.TaxId, loaded.TaxId);
            Assert.Equal(TaxIdType.Cif, loaded.TaxId.Type);
        }
    }

    [Fact]
    public async Task ClientCompany_CanBeQueriedByTaxId()
    {
        var taxId = TaxId.Create("12345678Z");

        await using (var db = fixture.CreateDbContext())
        {
            db.ClientCompanies.Add(ClientCompany.Create(fixture.Firm.Id, "Juan Pérez", taxId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            Assert.True(await db.ClientCompanies.AnyAsync(c => c.TaxId == taxId));
        }
    }

    [Fact]
    public async Task SavingDuplicateTaxId_ThrowsConflict()
    {
        var taxId = TaxId.Create("00000000T");

        await using var db = fixture.CreateDbContext();
        db.ClientCompanies.Add(ClientCompany.Create(fixture.Firm.Id, "First", taxId));
        await db.SaveChangesAsync();

        db.ClientCompanies.Add(ClientCompany.Create(fixture.Firm.Id, "Second", taxId));

        // Covers the race the handler's ExistsByTaxIdAsync check cannot: the unique index wins
        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ArchivedClientCompany_IsHiddenByQueryFilterButStillStored()
    {
        var company = ClientCompany.Create(fixture.Firm.Id, "Archived S.L.", TaxId.Create("87654321X"));
        company.Archive(DateTimeOffset.UtcNow);

        await using (var db = fixture.CreateDbContext())
        {
            db.ClientCompanies.Add(company);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Null(await db.ClientCompanies.FindAsync(company.Id));
            Assert.False(await db.ClientCompanies.AnyAsync(c => c.Id == company.Id));

            // Disabling only this named filter (EF Core 10) reaches the row again
            var stored = await db.ClientCompanies
                .IgnoreQueryFilters([QueryFilters.NotArchived])
                .SingleAsync(c => c.Id == company.Id);

            Assert.True(stored.IsArchived);
            Assert.Equal(company.ArchivedAt, stored.ArchivedAt);
        }
    }
}
