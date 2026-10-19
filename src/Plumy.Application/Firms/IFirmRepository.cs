using Plumy.Domain.Firms;

namespace Plumy.Application.Firms;

public interface IFirmRepository
{
    /// <summary>The firm of the user with this Entra object id, or null if they haven't joined one yet.</summary>
    Task<FirmMembership?> GetMembershipAsync(Guid userObjectId, CancellationToken cancellationToken = default);

    // Only tracks the firm (and its users): IUnitOfWork.SaveChangesAsync persists it
    void Add(Firm firm);
}
