using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Domain.Tests.Firms;

public class FirmInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly EmailAddress Email = EmailAddress.Create("luis@gestorialopez.es");

    private readonly Guid _ownerObjectId = Guid.NewGuid();
    private readonly Firm _firm;

    public FirmInvitationTests() => _firm = Firm.Create("Gestoría López", _ownerObjectId);

    [Fact]
    public void Invite_CreatesAnOpenInvitationThatExpiresInAWeek()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);

        Assert.Same(invitation, Assert.Single(_firm.Invitations));
        Assert.NotEqual(Guid.Empty, invitation.Id);
        Assert.Equal(_firm.Id, invitation.FirmId);
        Assert.Equal(Email, invitation.Email);
        Assert.Equal(_ownerObjectId, invitation.InvitedByObjectId);
        Assert.Equal(Now, invitation.CreatedAt);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.True(invitation.IsOpen);
        Assert.False(invitation.IsExpired(Now));
    }

    [Fact]
    public void Invite_SameAddressAgain_RenewsTheOpenInvitation()
    {
        var first = _firm.Invite(Email, _ownerObjectId, Now);

        var second = _firm.Invite(EmailAddress.Create("LUIS@gestorialopez.es"), _ownerObjectId, Now.AddDays(10));

        Assert.Same(first, second);
        Assert.Single(_firm.Invitations);
        Assert.Equal(Now, second.CreatedAt);
        Assert.Equal(Now.AddDays(17), second.ExpiresAt);
    }

    [Fact]
    public void Invite_AfterTheLastOneWasRevoked_CreatesANewOne()
    {
        var revoked = _firm.Invite(Email, _ownerObjectId, Now);
        _firm.RevokeInvitation(revoked.Id, Now);

        var invitation = _firm.Invite(Email, _ownerObjectId, Now);

        Assert.NotEqual(revoked.Id, invitation.Id);
        Assert.Equal(2, _firm.Invitations.Count);
    }

    [Fact]
    public void Invite_WithoutInviter_Throws()
    {
        Assert.Throws<DomainException>(() => _firm.Invite(Email, Guid.Empty, Now));
    }

    [Fact]
    public void IsExpired_FromTheExpiryOnwards()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);

        Assert.False(invitation.IsExpired(invitation.ExpiresAt.AddTicks(-1)));
        Assert.True(invitation.IsExpired(invitation.ExpiresAt));
    }

    [Fact]
    public void RevokeInvitation_ClosesIt()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);

        Assert.True(_firm.RevokeInvitation(invitation.Id, Now.AddHours(1)));

        Assert.False(invitation.IsOpen);
        Assert.Equal(Now.AddHours(1), invitation.RevokedAt);
    }

    [Fact]
    public void RevokeInvitation_UnknownOrAlreadyClosed_ReturnsFalse()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);
        _firm.RevokeInvitation(invitation.Id, Now);

        Assert.False(_firm.RevokeInvitation(invitation.Id, Now));
        Assert.False(_firm.RevokeInvitation(Guid.NewGuid(), Now));
    }

    [Fact]
    public void AcceptInvitation_AddsTheUserAsMember()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);
        var objectId = Guid.NewGuid();

        var member = _firm.AcceptInvitation(invitation.Id, objectId, Email, Now.AddDays(1));

        Assert.Equal(_firm.Id, member.FirmId);
        Assert.Equal(objectId, member.ObjectId);
        Assert.Equal(FirmRole.Member, member.Role);
        Assert.Contains(member, _firm.Users);
        Assert.False(invitation.IsOpen);
        Assert.Equal(Now.AddDays(1), invitation.AcceptedAt);
        Assert.Equal(objectId, invitation.AcceptedByObjectId);
    }

    [Fact]
    public void AcceptInvitation_Expired_ThrowsAndAddsNobody()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);

        Assert.Throws<DomainException>(
            () => _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), Email, invitation.ExpiresAt));

        Assert.Single(_firm.Users);
        Assert.True(invitation.IsOpen);
    }

    [Fact]
    public void AcceptInvitation_FromAnotherAddress_ThrowsAndAddsNobody()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);
        var someoneElse = EmailAddress.Create("intruso@example.com");

        Assert.Throws<DomainException>(() => _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), someoneElse, Now));

        Assert.Single(_firm.Users);
        Assert.True(invitation.IsOpen);
    }

    [Fact]
    public void AcceptInvitation_Revoked_Throws()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);
        _firm.RevokeInvitation(invitation.Id, Now);

        Assert.Throws<DomainException>(() => _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), Email, Now));
        Assert.Single(_firm.Users);
    }

    [Fact]
    public void AcceptInvitation_Twice_Throws()
    {
        var invitation = _firm.Invite(Email, _ownerObjectId, Now);
        _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), Email, Now);

        Assert.Throws<DomainException>(() => _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), Email, Now));
        Assert.Equal(2, _firm.Users.Count);
    }
}
