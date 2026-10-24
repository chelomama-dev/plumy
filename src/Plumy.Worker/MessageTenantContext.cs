using Plumy.Application.Abstractions;

namespace Plumy.Worker;

/// <summary>
/// The firm of the message being processed: one per message scope, set before anything touches the database.
/// The worker has no signed-in user, so the firm comes from the message (written by the API, which did check it).
/// </summary>
internal sealed class MessageTenantContext : ITenantContext
{
    private Guid? _firmId;

    // Fails closed, like the API's: a query before Set throws instead of running without a tenant
    public Guid FirmId => _firmId
        ?? throw new InvalidOperationException("The message's firm hasn't been set.");

    public void Set(Guid firmId)
    {
        if (firmId == Guid.Empty)
            throw new ArgumentException("A firm id is required.", nameof(firmId));
        if (_firmId is not null && _firmId != firmId)
            throw new InvalidOperationException("The message's firm can't change once set.");

        _firmId = firmId;
    }
}
