using Plumy.Domain.Common;
using Plumy.Domain.Firms;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class FirmMemberPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A firm of its own per test, with an owner and one member who joined by invitation.</summary>
    private async Task<(Firm Firm, Guid MemberObjectId)> CreateFirmWithMemberAsync()
    {
        var firm = Firm.Create("Gestoría López", Guid.NewGuid(), DateTimeOffset.UtcNow,
            new UserProfile("Ana López", EmailAddress.Create("ana@gestorialopez.es")));
        var email = EmailAddress.Create($"{Guid.NewGuid():N}@example.com");
        var memberObjectId = Guid.NewGuid();
        var invitation = firm.Invite(email, firm.Users.Single().ObjectId, Now);
        firm.AcceptInvitation(invitation.Id, memberObjectId, email, Now, "Luis Pérez");

        await using var db = fixture.CreateDbContext();
        db.Firms.Add(firm);
        await db.SaveChangesAsync();
        return (firm, memberObjectId);
    }

    [Fact]
    public async Task ListUsers_ReturnsTheFirmsPeopleWithTheirProfile()
    {
        var (firm, memberObjectId) = await CreateFirmWithMemberAsync();
        await CreateFirmWithMemberAsync();

        await using var db = fixture.CreateDbContext();
        var users = await new FirmRepository(db).ListUsersAsync(firm.Id);

        Assert.Equal(2, users.Count);
        var owner = users.Single(u => u.Role == FirmRole.Owner);
        Assert.Equal("Ana López", owner.Name);
        Assert.Equal("ana@gestorialopez.es", owner.Email?.Value);
        Assert.Equal("Luis Pérez", users.Single(u => u.ObjectId == memberObjectId).Name);
    }

    [Fact]
    public async Task RemoveMember_DeletesTheirRow()
    {
        var (firm, memberObjectId) = await CreateFirmWithMemberAsync();

        await using (var db = fixture.CreateDbContext())
        {
            var loaded = await new FirmRepository(db).GetWithUsersAsync(firm.Id);
            var member = loaded!.Users.Single(u => u.ObjectId == memberObjectId);
            Assert.True(loaded.RemoveMember(member.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var repository = new FirmRepository(db);
            Assert.Null(await repository.GetMembershipAsync(memberObjectId));
            Assert.Single(await repository.ListUsersAsync(firm.Id));
        }
    }

    [Fact]
    public async Task UpdateProfile_IsSaved_IncludingClearingIt()
    {
        var (_, memberObjectId) = await CreateFirmWithMemberAsync();

        await using (var db = fixture.CreateDbContext())
        {
            var user = await new FirmRepository(db).GetUserAsync(memberObjectId);
            Assert.True(user!.UpdateProfile(UserProfile.Empty));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var user = await new FirmRepository(db).GetUserAsync(memberObjectId);
            Assert.Null(user!.Name);
            Assert.Null(user.Email);
        }
    }
}
