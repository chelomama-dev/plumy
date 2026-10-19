using Microsoft.EntityFrameworkCore;
using Plumy.Application.Firms;
using Plumy.Domain.Firms;

namespace Plumy.Infrastructure.Persistence.Repositories;

internal sealed class FirmRepository(PlumyDbContext db) : IFirmRepository
{
    // Runs on every request to a tenant endpoint: one index seek on FirmUsers.ObjectId plus the firm by its key
    public Task<FirmMembership?> GetMembershipAsync(Guid userObjectId, CancellationToken cancellationToken = default) =>
        db.FirmUsers
            .Where(u => u.ObjectId == userObjectId)
            .Join(db.Firms, u => u.FirmId, f => f.Id, (u, f) => new FirmMembership(f.Id, f.Name, u.Role))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(Firm firm) => db.Firms.Add(firm);
}
