using Microsoft.EntityFrameworkCore;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Infrastructure.Tests.Persistence;

public class TenantIsolationPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task QueryFilter_HidesOtherFirmsCompanies()
    {
        var mine = ClientCompany.Create(fixture.Firm.Id, "Mía S.L.", TaxId.Create("B12345674"));
        var theirs = ClientCompany.Create(fixture.OtherFirm.Id, "Suya S.L.", TaxId.Create("12345678Z"));
        await SaveAsync(mine, theirs);

        // Same model, different tenant per context: the filter must read each context's firm,
        // not the one of the first context that built the (cached) model
        await using var myDb = fixture.CreateDbContext(fixture.Firm.Id);
        await using var theirDb = fixture.CreateDbContext(fixture.OtherFirm.Id);

        Assert.Equal([mine.Id], await myDb.ClientCompanies.Select(c => c.Id).ToListAsync());
        Assert.Null(await myDb.ClientCompanies.FindAsync(theirs.Id));
        Assert.Equal([theirs.Id], await theirDb.ClientCompanies.Select(c => c.Id).ToListAsync());
    }

    [Fact]
    public async Task SameTaxId_CanBelongToTwoFirms()
    {
        var taxId = TaxId.Create("00000000T");

        // The unique index is (FirmId, TaxId): two firms may keep the books of the same company
        await SaveAsync(
            ClientCompany.Create(fixture.Firm.Id, "Acme S.L.", taxId),
            ClientCompany.Create(fixture.OtherFirm.Id, "Acme S.L.", taxId));
    }

    [Fact]
    public async Task UserInTwoFirms_ThrowsConflict()
    {
        var objectId = Guid.NewGuid();

        await using var db = fixture.CreateDbContext();
        db.Firms.Add(Firm.Create("Primera", objectId));
        await db.SaveChangesAsync();

        db.Firms.Add(Firm.Create("Segunda", objectId));

        // The unique index on ObjectId is what really stops two concurrent onboardings by the same user
        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task FirmWithUsers_IsReadBackWithItsMembership()
    {
        var firm = Firm.Create("Gestoría Ruiz", Guid.NewGuid());

        await using (var db = fixture.CreateDbContext())
        {
            db.Firms.Add(firm);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var loaded = await db.Firms.Include(f => f.Users).SingleAsync(f => f.Id == firm.Id);

            var owner = Assert.Single(loaded.Users);
            Assert.Equal(firm.Users.Single().ObjectId, owner.ObjectId);
            Assert.Equal(FirmRole.Owner, owner.Role);
        }
    }

    // Saved through a context without filters in play: inserts don't go through query filters
    private async Task SaveAsync(params ClientCompany[] companies)
    {
        await using var db = fixture.CreateDbContext();
        db.ClientCompanies.AddRange(companies);
        await db.SaveChangesAsync();
    }
}
