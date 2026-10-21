using Microsoft.EntityFrameworkCore;
using Plumy.Application.Common;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;
using Plumy.Infrastructure.Persistence;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class FirmInvitationPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    // Each test invites its own address, since they all share the fixture's firms
    private static EmailAddress NewEmail() => EmailAddress.Create($"{Guid.NewGuid():N}@example.com");

    private Guid OwnerOf(Firm firm) => firm.Users.Single().ObjectId;

    [Fact]
    public async Task GetWithOpenInvitations_LoadsOnlyOpenInvitations()
    {
        var (open, revoked, accepted) = (NewEmail(), NewEmail(), NewEmail());
        await using (var db = fixture.CreateDbContext())
        {
            var firm = await new FirmRepository(db).GetWithOpenInvitationsAsync(fixture.Firm.Id);
            firm!.Invite(open, OwnerOf(fixture.Firm), Now);
            firm.RevokeInvitation(firm.Invite(revoked, OwnerOf(fixture.Firm), Now).Id, Now);
            firm.AcceptInvitation(firm.Invite(accepted, OwnerOf(fixture.Firm), Now).Id, Guid.NewGuid(), accepted, Now);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var firm = await new FirmRepository(db).GetWithOpenInvitationsAsync(fixture.Firm.Id);

            var emails = firm!.Invitations.Select(i => i.Email).ToList();
            Assert.Contains(open, emails);
            Assert.DoesNotContain(revoked, emails);
            Assert.DoesNotContain(accepted, emails);
        }
    }

    [Fact]
    public async Task Invitation_RoundTripsEveryField()
    {
        var email = NewEmail();
        FirmInvitation sent;
        await using (var db = fixture.CreateDbContext())
        {
            var firm = await new FirmRepository(db).GetWithOpenInvitationsAsync(fixture.Firm.Id);
            sent = firm!.Invite(email, OwnerOf(fixture.Firm), Now);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var loaded = await db.FirmInvitations.SingleAsync(i => i.Id == sent.Id);

            Assert.Equal(fixture.Firm.Id, loaded.FirmId);
            Assert.Equal(email, loaded.Email);
            Assert.Equal(sent.InvitedByObjectId, loaded.InvitedByObjectId);
            Assert.Equal(Now, loaded.CreatedAt);
            Assert.Equal(sent.ExpiresAt, loaded.ExpiresAt);
            Assert.True(loaded.IsOpen);
        }
    }

    [Fact]
    public async Task AcceptInvitation_SavesTheNewMemberAndClosesTheInvitation()
    {
        var email = NewEmail();
        var invitationId = await InviteAsync(fixture.Firm, email);
        var objectId = Guid.NewGuid();

        await using (var db = fixture.CreateDbContext())
        {
            // Loaded without its users: the new member must still be inserted, not treated as an existing row
            var firm = await new FirmRepository(db).GetByOpenInvitationAsync(invitationId, email);
            firm!.AcceptInvitation(invitationId, objectId, email, Now);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var membership = await new FirmRepository(db).GetMembershipAsync(objectId);
            Assert.Equal(new(fixture.Firm.Id, fixture.Firm.Name, FirmRole.Member), membership);

            var invitation = await db.FirmInvitations.SingleAsync(i => i.Id == invitationId);
            Assert.Equal(Now, invitation.AcceptedAt);
            Assert.Equal(objectId, invitation.AcceptedByObjectId);
        }
    }

    [Fact]
    public async Task GetByOpenInvitation_WithAnotherEmailOrClosed_ReturnsNull()
    {
        var email = NewEmail();
        var invitationId = await InviteAsync(fixture.Firm, email);

        await using (var db = fixture.CreateDbContext())
        {
            var repository = new FirmRepository(db);
            Assert.Null(await repository.GetByOpenInvitationAsync(invitationId, NewEmail()));
            Assert.Null(await repository.GetByOpenInvitationAsync(Guid.NewGuid(), email));

            var firm = await repository.GetByOpenInvitationAsync(invitationId, email);
            Assert.Equal(invitationId, Assert.Single(firm!.Invitations).Id);
            firm.RevokeInvitation(invitationId, Now);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Null(await new FirmRepository(db).GetByOpenInvitationAsync(invitationId, email));
        }
    }

    [Fact]
    public async Task ListPendingInvitations_ComesFromEveryFirm_AndSkipsExpiredOnes()
    {
        var email = NewEmail();
        var mine = await InviteAsync(fixture.Firm, email);
        var theirs = await InviteAsync(fixture.OtherFirm, email);
        await InviteAsync(fixture.Firm, NewEmail());

        // The context's tenant doesn't matter: the user has no firm yet when this runs
        await using var db = fixture.CreateDbContext();
        var repository = new FirmRepository(db);

        var pending = await repository.ListPendingInvitationsAsync(email, Now);
        // Both were sent at the same time, so their order is arbitrary
        Assert.Equal(
            new[] { (mine, fixture.Firm.Name), (theirs, fixture.OtherFirm.Name) }.Order(),
            pending.Select(p => (p.Id, p.FirmName)).Order());

        Assert.Empty(await repository.ListPendingInvitationsAsync(email, Now + FirmInvitation.ValidFor));
    }

    [Fact]
    public async Task TwoOpenInvitationsForTheSameAddress_ThrowsConflict()
    {
        var email = NewEmail();

        // Two requests that both loaded the firm before either saved: each one thinks it's the first invitation
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var firstFirm = await new FirmRepository(first).GetWithOpenInvitationsAsync(fixture.Firm.Id);
        var secondFirm = await new FirmRepository(second).GetWithOpenInvitationsAsync(fixture.Firm.Id);

        firstFirm!.Invite(email, OwnerOf(fixture.Firm), Now);
        secondFirm!.Invite(email, OwnerOf(fixture.Firm), Now);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task SameAddress_CanBeInvitedByTwoFirms_AndAgainOnceRevoked()
    {
        var email = NewEmail();
        var revoked = await InviteAsync(fixture.Firm, email);
        await InviteAsync(fixture.OtherFirm, email);

        await using (var db = fixture.CreateDbContext())
        {
            var firm = await new FirmRepository(db).GetWithOpenInvitationsAsync(fixture.Firm.Id);
            firm!.RevokeInvitation(revoked, Now);
            firm.Invite(email, OwnerOf(fixture.Firm), Now);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(3, await db.FirmInvitations.CountAsync(i => i.Email == email));
        }
    }

    private async Task<Guid> InviteAsync(Firm firm, EmailAddress email)
    {
        await using var db = fixture.CreateDbContext();
        var loaded = await new FirmRepository(db).GetWithOpenInvitationsAsync(firm.Id);
        var invitation = loaded!.Invite(email, OwnerOf(firm), Now);
        await db.SaveChangesAsync();
        return invitation.Id;
    }
}
