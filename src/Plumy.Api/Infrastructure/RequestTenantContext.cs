using Plumy.Application.Abstractions;
using Plumy.Application.Firms;
using Plumy.Domain.Firms;

namespace Plumy.Api.Infrastructure;

/// <summary>
/// The firm of the current request and the user's role in it, set once by <see cref="FirmMembershipFilter"/>
/// before the endpoint runs.
/// </summary>
internal sealed class RequestTenantContext : ITenantContext
{
    private FirmMembership? _membership;

    // Fails closed: an endpoint that forgot RequireFirmMembership() gets a 500, never another firm's data
    public Guid FirmId => Membership.FirmId;

    public FirmRole Role => Membership.Role;

    private FirmMembership Membership => _membership
        ?? throw new InvalidOperationException(
            "The request has no firm. Add RequireFirmMembership() to the endpoint group.");

    public void SetMembership(FirmMembership membership)
    {
        if (_membership is not null && _membership.FirmId != membership.FirmId)
            throw new InvalidOperationException("The request's firm can't change once set.");

        _membership = membership;
    }
}
