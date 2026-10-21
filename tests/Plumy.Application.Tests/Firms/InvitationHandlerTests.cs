using Plumy.Application.Common;
using Plumy.Application.Firms.AcceptInvitation;
using Plumy.Application.Firms.GetCurrentUser;
using Plumy.Application.Firms.InviteMember;
using Plumy.Application.Firms.ListInvitations;
using Plumy.Application.Firms.RevokeInvitation;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Tests.Firms;

public class InvitationHandlerTests
{
    private const string InviteeEmail = "luis@gestorialopez.es";

    private readonly InMemoryFirmStore _store = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeCurrentUser _owner = new();
    private readonly FakeCurrentUser _invitee = new() { Name = "Luis Pérez", Email = InviteeEmail };
    private readonly Firm _firm;

    public InvitationHandlerTests() => _firm = _store.Seed("Gestoría López", _owner.ObjectId);

    private InviteMemberHandler Invite() =>
        new(new FakeTenant(_firm.Id), _owner, _store, _store, _clock);

    private ListInvitationsHandler List() => new(new FakeTenant(_firm.Id), _store, _clock);

    private RevokeInvitationHandler Revoke() => new(new FakeTenant(_firm.Id), _store, _store, _clock);

    private AcceptInvitationHandler Accept(FakeCurrentUser user) => new(user, _store, _store, _clock);

    private GetCurrentUserHandler Me(FakeCurrentUser user) => new(user, _store, _clock);

    [Fact]
    public async Task Invite_CreatesAnInvitationForTheNormalizedAddress()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand("  Luis@GestoriaLopez.es "));

        Assert.Equal(InviteeEmail, invitation.Email);
        Assert.Equal(_clock.Now, invitation.CreatedAt);
        Assert.Equal(_clock.Now.AddDays(7), invitation.ExpiresAt);
        Assert.False(invitation.IsExpired);
        Assert.Equal(_owner.ObjectId, Assert.Single(_firm.Invitations).InvitedByObjectId);
        Assert.Equal(1, _store.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not an email")]
    public async Task Invite_InvalidAddress_ThrowsAndDoesNotSave(string? email)
    {
        await Assert.ThrowsAsync<DomainException>(() => Invite().HandleAsync(new InviteMemberCommand(email)));

        Assert.Empty(_firm.Invitations);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Invite_Themselves_Throws()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => Invite().HandleAsync(new InviteMemberCommand("ANA@gestorialopez.es")));

        Assert.Empty(_firm.Invitations);
    }

    [Fact]
    public async Task List_ShowsOpenInvitationsNewestFirst_AndWhichHaveExpired()
    {
        var old = await Invite().HandleAsync(new InviteMemberCommand("vieja@example.com"));
        _clock.Now = _clock.Now.AddDays(8);
        var recent = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        var revoked = await Invite().HandleAsync(new InviteMemberCommand("revocada@example.com"));
        await Revoke().HandleAsync(revoked.Id);

        var invitations = await List().HandleAsync();

        Assert.Equal([recent.Id, old.Id], invitations.Select(i => i.Id));
        Assert.False(invitations[0].IsExpired);
        Assert.True(invitations[1].IsExpired);
    }

    [Fact]
    public async Task Revoke_OpenInvitation_ClosesItAndSaves()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));

        Assert.True(await Revoke().HandleAsync(invitation.Id));

        Assert.False(_firm.Invitations.Single().IsOpen);
        Assert.Equal(2, _store.SaveCount);
    }

    [Fact]
    public async Task Revoke_UnknownInvitation_ReturnsFalseWithoutSaving()
    {
        Assert.False(await Revoke().HandleAsync(Guid.NewGuid()));
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Me_UserWithoutFirm_SeesTheInvitationsSentToTheirEmail()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        await Invite().HandleAsync(new InviteMemberCommand("otra.persona@example.com"));

        var me = await Me(_invitee).HandleAsync();

        var pending = Assert.Single(me.Invitations);
        Assert.Equal(invitation.Id, pending.Id);
        Assert.Equal("Gestoría López", pending.FirmName);
        Assert.Equal(invitation.ExpiresAt, pending.ExpiresAt);
    }

    [Fact]
    public async Task Me_DoesNotShowExpiredInvitations()
    {
        await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        _clock.Now = _clock.Now.Add(FirmInvitation.ValidFor);

        Assert.Empty((await Me(_invitee).HandleAsync()).Invitations);
    }

    [Fact]
    public async Task Me_UserInAFirm_SeesNoInvitations()
    {
        await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        _store.Seed("Su propia gestoría", _invitee.ObjectId);

        Assert.Empty((await Me(_invitee).HandleAsync()).Invitations);
    }

    [Fact]
    public async Task Accept_JoinsTheFirmAsMember()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));

        var membership = await Accept(_invitee).HandleAsync(invitation.Id);

        Assert.NotNull(membership);
        Assert.Equal(_firm.Id, membership.Id);
        Assert.Equal("Gestoría López", membership.Name);
        Assert.Equal("Member", membership.Role);
        Assert.Contains(_firm.Users, u => u.ObjectId == _invitee.ObjectId && u.Role == FirmRole.Member);
        Assert.Equal(2, _store.SaveCount);

        var me = await Me(_invitee).HandleAsync();
        Assert.Equal(membership, me.Firm);
        Assert.Empty(me.Invitations);
    }

    [Fact]
    public async Task Accept_SomeoneElsesInvitation_ReturnsNull()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        var intruder = new FakeCurrentUser { Email = "intruso@example.com" };

        Assert.Null(await Accept(intruder).HandleAsync(invitation.Id));

        Assert.Single(_firm.Users);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Accept_RevokedInvitation_ReturnsNull()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        await Revoke().HandleAsync(invitation.Id);

        Assert.Null(await Accept(_invitee).HandleAsync(invitation.Id));
        Assert.Single(_firm.Users);
    }

    [Fact]
    public async Task Accept_ExpiredInvitation_ThrowsAndDoesNotSave()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        _clock.Now = invitation.ExpiresAt;

        await Assert.ThrowsAsync<DomainException>(() => Accept(_invitee).HandleAsync(invitation.Id));

        Assert.Single(_firm.Users);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Accept_UserAlreadyInAFirm_ThrowsConflict()
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        _store.Seed("Su propia gestoría", _invitee.ObjectId);

        await Assert.ThrowsAsync<ConflictException>(() => Accept(_invitee).HandleAsync(invitation.Id));

        Assert.Single(_firm.Users);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-email")]
    public async Task Accept_AccountWithoutEmail_Throws(string? email)
    {
        var invitation = await Invite().HandleAsync(new InviteMemberCommand(InviteeEmail));
        var user = new FakeCurrentUser { Email = email };

        await Assert.ThrowsAsync<DomainException>(() => Accept(user).HandleAsync(invitation.Id));
    }
}
