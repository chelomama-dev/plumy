using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Firms.AcceptInvitation;

/// <summary>
/// A signed-in user without a firm joins the one that invited them. The invitation must have been sent to the
/// email of their account, which Entra External ID verified with a one-time code when they signed up.
/// </summary>
public sealed class AcceptInvitationHandler(
    ICurrentUser currentUser, IFirmRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    /// <returns>The user's new membership, or null if there is no open invitation with that id for them.</returns>
    public async Task<FirmMembershipResponse?> HandleAsync(
        Guid invitationId, CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryCreate(currentUser.Email, out var email))
            throw new DomainException("Your account has no email address, so it can't accept invitations.");

        // Fast path with a friendly message; the unique index on the user's object id is the real guarantee
        if (await repository.GetMembershipAsync(currentUser.ObjectId, cancellationToken) is not null)
            throw new ConflictException("You already belong to an accounting firm.");

        var firm = await repository.GetByOpenInvitationAsync(invitationId, email, cancellationToken);
        if (firm is null)
            return null;

        firm.AcceptInvitation(invitationId, currentUser.ObjectId, email, timeProvider.GetUtcNow(), currentUser.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FirmMembershipResponse.From(new FirmMembership(firm.Id, firm.Name, FirmRole.Member));
    }
}
