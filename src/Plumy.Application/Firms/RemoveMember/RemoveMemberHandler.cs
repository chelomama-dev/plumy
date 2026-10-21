using Plumy.Application.Abstractions;

namespace Plumy.Application.Firms.RemoveMember;

/// <summary>
/// The owner takes someone out of the firm. Their next request gets a 403 and they can create or join another
/// firm. False if the firm has nobody with that id. Only owners get here: the API checks the role first.
/// </summary>
public sealed class RemoveMemberHandler(ITenantContext tenant, IFirmRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<bool> HandleAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var firm = await repository.GetWithUsersAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        if (!firm.RemoveMember(memberId))
            return false;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
