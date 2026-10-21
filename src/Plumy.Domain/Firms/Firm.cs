using Plumy.Domain.Common;

namespace Plumy.Domain.Firms;

/// <summary>
/// An accounting firm (gestoría): the tenant. Every client company and every user belongs to exactly one.
/// </summary>
public sealed class Firm
{
    public const int MaxNameLength = 200;

    private readonly List<FirmUser> _users = [];
    private readonly List<FirmInvitation> _invitations = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public IReadOnlyCollection<FirmUser> Users => _users.AsReadOnly();

    /// <summary>
    /// Invitations to join the firm. Repositories load only the open ones (neither accepted nor revoked),
    /// which are all the rules below need: the closed ones are history.
    /// </summary>
    public IReadOnlyCollection<FirmInvitation> Invitations => _invitations.AsReadOnly();

    private Firm(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Self-service onboarding: whoever creates the firm becomes its owner.</summary>
    public static Firm Create(string? name, Guid ownerObjectId, UserProfile? ownerProfile = null)
    {
        var firm = new Firm(Guid.CreateVersion7(), ValidateName(name));
        firm._users.Add(FirmUser.Create(firm.Id, ownerObjectId, FirmRole.Owner, ownerProfile ?? UserProfile.Empty));
        return firm;
    }

    /// <summary>
    /// Invites someone to join as a member. If the address already has an open invitation, that one is renewed
    /// instead, so inviting twice is harmless and is how an owner re-sends an expired invitation.
    /// </summary>
    public FirmInvitation Invite(EmailAddress email, Guid invitedByObjectId, DateTimeOffset now)
    {
        var open = _invitations.SingleOrDefault(i => i.IsOpen && i.Email == email);
        if (open is not null)
        {
            open.Renew(now);
            return open;
        }

        var invitation = FirmInvitation.Create(Id, email, invitedByObjectId, now);
        _invitations.Add(invitation);
        return invitation;
    }

    /// <summary>Withdraws an open invitation. False if the firm has no open invitation with that id.</summary>
    public bool RevokeInvitation(Guid invitationId, DateTimeOffset now)
    {
        var invitation = _invitations.SingleOrDefault(i => i.Id == invitationId && i.IsOpen);
        invitation?.Revoke(now);
        return invitation is not null;
    }

    /// <summary>
    /// The invited person joins the firm as a member. Only the address the invitation was sent to can accept it.
    /// That a user belongs to one firm at most is checked by whoever stores the firm (a unique index).
    /// </summary>
    public FirmUser AcceptInvitation(
        Guid invitationId, Guid objectId, EmailAddress email, DateTimeOffset now, string? name = null)
    {
        var invitation = _invitations.SingleOrDefault(i => i.Id == invitationId && i.IsOpen)
            ?? throw new DomainException("The invitation doesn't exist or is no longer open.");

        if (invitation.Email != email)
            throw new DomainException("This invitation was sent to another email address.");

        invitation.Accept(objectId, now);

        var member = FirmUser.Create(Id, objectId, FirmRole.Member, new UserProfile(name, email));
        _users.Add(member);
        return member;
    }

    /// <summary>
    /// Takes a member out of the firm: they lose access to its data and can create or join another firm.
    /// Needs the firm loaded with its users. False if the firm has no user with that id.
    /// </summary>
    public bool RemoveMember(Guid userId)
    {
        var user = _users.SingleOrDefault(u => u.Id == userId);
        if (user is null)
            return false;

        // The firm always keeps its owner: nobody else can manage its people
        if (user.Role == FirmRole.Owner)
            throw new DomainException("The firm's owner can't be removed.");

        _users.Remove(user);
        return true;
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Firm name is required.");

        var trimmed = name.Trim();

        if (trimmed.Length > MaxNameLength)
            throw new DomainException($"Firm name cannot exceed {MaxNameLength} characters.");

        return trimmed;
    }
}
