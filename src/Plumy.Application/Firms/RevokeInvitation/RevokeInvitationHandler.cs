using Plumy.Application.Abstractions;

namespace Plumy.Application.Firms.RevokeInvitation;

/// <summary>The owner withdraws an open invitation. False if the firm has no open invitation with that id.</summary>
public sealed class RevokeInvitationHandler(
    ITenantContext tenant, IFirmRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<bool> HandleAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var firm = await repository.GetWithOpenInvitationsAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        if (!firm.RevokeInvitation(invitationId, timeProvider.GetUtcNow()))
            return false;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
