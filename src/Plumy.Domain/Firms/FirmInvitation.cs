using Plumy.Domain.Common;

namespace Plumy.Domain.Firms;

/// <summary>
/// An invitation for someone to join a firm as a member. Plumy doesn't email it: whoever signs in with
/// this address sees it and can accept it. The firm's owner tells them to sign up.
/// </summary>
public sealed class FirmInvitation
{
    /// <summary>How long an invitation can be accepted for. Inviting the same address again renews it.</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromDays(7);

    public Guid Id { get; private set; }
    public Guid FirmId { get; private set; }
    public EmailAddress Email { get; private set; }

    /// <summary>Object id of the owner who sent it.</summary>
    public Guid InvitedByObjectId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>Who accepted it. Kept because the email is only the address it was sent to.</summary>
    public Guid? AcceptedByObjectId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Neither accepted nor revoked. It may still have expired: see <see cref="IsExpired"/>.</summary>
    public bool IsOpen => AcceptedAt is null && RevokedAt is null;

    private FirmInvitation(Guid id, Guid firmId, EmailAddress email, Guid invitedByObjectId, DateTimeOffset now)
    {
        Id = id;
        FirmId = firmId;
        Email = email;
        InvitedByObjectId = invitedByObjectId;
        CreatedAt = now;
        ExpiresAt = now + ValidFor;
    }

    // EF Core: it can't bind EmailAddress through a constructor parameter, so it sets the property afterwards
    private FirmInvitation()
    {
        Email = null!;
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    // Only a Firm creates and changes invitations, so it can keep one open invitation per address
    internal static FirmInvitation Create(Guid firmId, EmailAddress email, Guid invitedByObjectId, DateTimeOffset now)
    {
        if (invitedByObjectId == Guid.Empty)
            throw new DomainException("The inviting user's object id is required.");

        return new FirmInvitation(Guid.CreateVersion7(), firmId, email, invitedByObjectId, now);
    }

    internal void Renew(DateTimeOffset now) => ExpiresAt = now + ValidFor;

    internal void Revoke(DateTimeOffset now) => RevokedAt = now;

    internal void Accept(Guid objectId, DateTimeOffset now)
    {
        if (IsExpired(now))
            throw new DomainException("This invitation has expired. Ask the firm to invite you again.");

        AcceptedAt = now;
        AcceptedByObjectId = objectId;
    }
}
