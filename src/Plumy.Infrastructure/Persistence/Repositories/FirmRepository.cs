using Microsoft.EntityFrameworkCore;
using Plumy.Application.Firms;
using Plumy.Domain.Common;
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

    // Filtered include: accepted and revoked invitations pile up over time and no rule needs them
    public Task<Firm?> GetWithOpenInvitationsAsync(Guid firmId, CancellationToken cancellationToken = default) =>
        db.Firms
            .Include(f => f.Invitations.Where(i => i.AcceptedAt == null && i.RevokedAt == null))
            .SingleOrDefaultAsync(f => f.Id == firmId, cancellationToken);

    public Task<Firm?> GetByOpenInvitationAsync(
        Guid invitationId, EmailAddress email, CancellationToken cancellationToken = default) =>
        db.Firms
            .Include(f => f.Invitations.Where(i => i.Id == invitationId))
            .Where(f => f.Invitations.Any(i =>
                i.Id == invitationId && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null))
            .SingleOrDefaultAsync(cancellationToken);

    // Across every firm on purpose: the user has no firm yet, so there is no tenant to scope it to.
    // Only the invitations sent to their own (verified) address come back
    public async Task<IReadOnlyList<PendingInvitation>> ListPendingInvitationsAsync(
        EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await db.FirmInvitations
            .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .Join(db.Firms, i => i.FirmId, f => f.Id, (i, f) => new { Invitation = i, FirmName = f.Name })
            .OrderByDescending(x => x.Invitation.CreatedAt)
            .Select(x => new PendingInvitation(x.Invitation.Id, x.FirmName, x.Invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

    public void Add(Firm firm) => db.Firms.Add(firm);
}
