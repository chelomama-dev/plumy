using Plumy.Application.Abstractions;

namespace Plumy.Application.Firms.GetCurrentUser;

public sealed class GetCurrentUserHandler(ICurrentUser currentUser, IFirmRepository repository)
{
    public async Task<CurrentUserResponse> HandleAsync(CancellationToken cancellationToken = default)
    {
        var membership = await repository.GetMembershipAsync(currentUser.ObjectId, cancellationToken);

        return new CurrentUserResponse(
            currentUser.Name,
            currentUser.Email,
            membership is null ? null : FirmMembershipResponse.From(membership));
    }
}
