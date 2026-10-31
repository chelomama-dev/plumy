using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Firms.CreateFirm;

/// <summary>Onboarding: a signed-in user without a firm creates one and becomes its owner.</summary>
public sealed class CreateFirmHandler(
    ICurrentUser currentUser, IFirmRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<FirmMembershipResponse> HandleAsync(
        CreateFirmCommand command, CancellationToken cancellationToken = default)
    {
        // Fast path with a friendly message; the unique index on the user's object id is the real guarantee
        if (await repository.GetMembershipAsync(currentUser.ObjectId, cancellationToken) is not null)
            throw new ConflictException("You already belong to an accounting firm.");

        var firm = Firm.Create(
            command.Name, currentUser.ObjectId, clock.GetUtcNow(),
            UserProfile.FromClaims(currentUser.Name, currentUser.Email));

        repository.Add(firm);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FirmMembershipResponse.From(new FirmMembership(firm.Id, firm.Name, FirmRole.Owner));
    }
}
