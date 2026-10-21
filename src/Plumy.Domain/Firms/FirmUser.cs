using Plumy.Domain.Common;

namespace Plumy.Domain.Firms;

/// <summary>
/// A person who works in an accounting firm. Entra says who they are; this says which firm they belong to.
/// </summary>
public sealed class FirmUser
{
    public Guid Id { get; private set; }
    public Guid FirmId { get; private set; }

    /// <summary>The user's object id in Entra External ID (<c>oid</c> claim): stable and never reused.</summary>
    public Guid ObjectId { get; private set; }

    public FirmRole Role { get; private set; }

    /// <summary>Copied from their account so the team can tell who is who. Null until they sign in again.</summary>
    public string? Name { get; private set; }

    public EmailAddress? Email { get; private set; }

    private FirmUser(Guid id, Guid firmId, Guid objectId, FirmRole role)
    {
        Id = id;
        FirmId = firmId;
        ObjectId = objectId;
        Role = role;
    }

    public UserProfile Profile => new(Name, Email);

    /// <summary>Keeps the copy of the user's name and email current. False if nothing changed.</summary>
    public bool UpdateProfile(UserProfile profile)
    {
        if (profile == Profile)
            return false;

        Name = profile.Name;
        Email = profile.Email;
        return true;
    }

    // Only a Firm adds users, so the firm stays in charge of its own membership rules
    internal static FirmUser Create(Guid firmId, Guid objectId, FirmRole role, UserProfile profile)
    {
        if (objectId == Guid.Empty)
            throw new DomainException("The user's object id is required.");

        var user = new FirmUser(Guid.CreateVersion7(), firmId, objectId, role);
        user.UpdateProfile(profile);
        return user;
    }
}
