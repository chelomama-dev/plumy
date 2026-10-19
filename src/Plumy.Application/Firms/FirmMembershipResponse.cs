namespace Plumy.Application.Firms;

public sealed record FirmMembershipResponse(Guid Id, string Name, string Role)
{
    public static FirmMembershipResponse From(FirmMembership membership) =>
        new(membership.FirmId, membership.FirmName, membership.Role.ToString());
}
