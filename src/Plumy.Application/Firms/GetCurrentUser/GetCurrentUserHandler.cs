using Plumy.Application.Abstractions;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Firms.GetCurrentUser;

public sealed class GetCurrentUserHandler(
    ICurrentUser currentUser, IFirmRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<CurrentUserResponse> HandleAsync(CancellationToken cancellationToken = default)
    {
        var membership = await repository.GetMembershipAsync(currentUser.ObjectId, cancellationToken);

        if (membership is not null)
            await SyncProfileAsync(cancellationToken);

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

    // The SPA calls /api/me once per session, right after sign-in: a good moment to refresh the name and email
    // the team sees, in case they changed in Entra. It writes only when something did change, so it stays a
    // read almost always (and users who joined before profiles were stored get theirs on their next visit)
    private async Task SyncProfileAsync(CancellationToken cancellationToken)
    {
        var user = await repository.GetUserAsync(currentUser.ObjectId, cancellationToken);

        if (user is not null && user.UpdateProfile(UserProfile.FromClaims(currentUser.Name, currentUser.Email)))
            await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
