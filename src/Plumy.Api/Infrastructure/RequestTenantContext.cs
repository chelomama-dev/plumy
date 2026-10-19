using Plumy.Application.Abstractions;

namespace Plumy.Api.Infrastructure;

/// <summary>
/// The firm of the current request, set once by <see cref="FirmMembershipFilter"/> before the endpoint runs.
/// </summary>
internal sealed class RequestTenantContext : ITenantContext
{
    private Guid? _firmId;

    // Fails closed: an endpoint that forgot RequireFirmMembership() gets a 500, never another firm's data
    public Guid FirmId => _firmId
        ?? throw new InvalidOperationException(
            "The request has no firm. Add RequireFirmMembership() to the endpoint group.");

    public void SetFirm(Guid firmId)
    {
        if (_firmId is not null && _firmId != firmId)
            throw new InvalidOperationException("The request's firm can't change once set.");

        _firmId = firmId;
    }
}
