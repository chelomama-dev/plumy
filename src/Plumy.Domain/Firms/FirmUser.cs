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

    private FirmUser(Guid id, Guid firmId, Guid objectId, FirmRole role)
    {
        Id = id;
        FirmId = firmId;
        ObjectId = objectId;
        Role = role;
    }

    // Only a Firm adds users, so the firm stays in charge of its own membership rules
    internal static FirmUser Create(Guid firmId, Guid objectId, FirmRole role)
    {
        if (objectId == Guid.Empty)
            throw new DomainException("The user's object id is required.");

        return new FirmUser(Guid.CreateVersion7(), firmId, objectId, role);
    }
}
