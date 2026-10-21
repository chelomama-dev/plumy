using Plumy.Application.Abstractions;
using Plumy.Domain.Common;

namespace Plumy.Application.Firms.GetCurrentUser;

public sealed class GetCurrentUserHandler(
    ICurrentUser currentUser, IFirmRepository repository, TimeProvider timeProvider)
{
    public async Task<CurrentUserResponse> HandleAsync(CancellationToken cancellationToken = default)
    {
        var membership = await repository.GetMembershipAsync(currentUser.ObjectId, cancellationToken);

        // Invitations only matter to someone who can still accept one: a user belongs to one firm at most
        IReadOnlyList<PendingInvitation> invitations =
            membership is null && EmailAddress.TryCreate(currentUser.Email, out var email)
                ? await repository.ListPendingInvitationsAsync(email, timeProvider.GetUtcNow(), cancellationToken)
                : [];

        return new CurrentUserResponse(
            currentUser.Name,
            currentUser.Email,
            membership is null ? null : FirmMembershipResponse.From(membership),
            invitations);
    }
}
