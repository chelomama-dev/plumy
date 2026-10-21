using Plumy.Application.Abstractions;

namespace Plumy.Application.Firms.ListInvitations;

/// <summary>The current firm's open invitations, newest first, including expired ones the owner may renew.</summary>
public sealed class ListInvitationsHandler(
    ITenantContext tenant, IFirmRepository repository, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<InvitationResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var firm = await repository.GetWithOpenInvitationsAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        var now = timeProvider.GetUtcNow();

        // The repository already loads only open ones; filtering again keeps this right whatever it loads
        return firm.Invitations
            .Where(i => i.IsOpen)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => InvitationResponse.From(i, now))
            .ToList();
    }
}
