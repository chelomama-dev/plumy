using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Firms;

public interface IFirmRepository
{
    /// <summary>The firm of the user with this Entra object id, or null if they haven't joined one yet.</summary>
    Task<FirmMembership?> GetMembershipAsync(Guid userObjectId, CancellationToken cancellationToken = default);

    /// <summary>The firm with its open invitations (not its users), tracked for changes.</summary>
    Task<Firm?> GetWithOpenInvitationsAsync(Guid firmId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The firm that sent this open invitation to this address, with only that invitation loaded, tracked for
    /// changes. Null if there is no such invitation, so a guessed id is useless without the matching email.
    /// </summary>
    Task<Firm?> GetByOpenInvitationAsync(
        Guid invitationId, EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>Open, unexpired invitations sent to this address by any firm.</summary>
    Task<IReadOnlyList<PendingInvitation>> ListPendingInvitationsAsync(
        EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken = default);

    // Only tracks the firm (and its users): IUnitOfWork.SaveChangesAsync persists it
    void Add(Firm firm);
}
