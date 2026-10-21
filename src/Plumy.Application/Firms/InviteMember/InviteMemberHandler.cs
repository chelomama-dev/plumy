using Plumy.Application.Abstractions;
using Plumy.Domain.Common;

namespace Plumy.Application.Firms.InviteMember;

/// <summary>
/// The firm's owner invites someone by email. Inviting an address with an open invitation renews it.
/// Only owners get here: the API checks the role before the handler runs.
/// </summary>
public sealed class InviteMemberHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IFirmRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<InvitationResponse> HandleAsync(
        InviteMemberCommand command, CancellationToken cancellationToken = default)
    {
        var email = EmailAddress.Create(command.Email);

        // Other members' addresses aren't stored, so this is the only "already in the firm" case we can spot;
        // accepting fails anyway for anyone who belongs to a firm
        if (EmailAddress.TryCreate(currentUser.Email, out var ownEmail) && ownEmail == email)
            throw new DomainException("You can't invite yourself.");

        var firm = await repository.GetWithOpenInvitationsAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        var now = timeProvider.GetUtcNow();
        var invitation = firm.Invite(email, currentUser.ObjectId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InvitationResponse.From(invitation, now);
    }
}
