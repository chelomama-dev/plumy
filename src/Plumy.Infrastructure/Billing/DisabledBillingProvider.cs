using Plumy.Application.Billing;
using Plumy.Domain.Billing;

namespace Plumy.Infrastructure.Billing;

/// <summary>
/// Stands in for Stripe where it isn't set up (no Stripe:SecretKey): the plans and the trial work, paying doesn't.
/// </summary>
internal sealed class DisabledBillingProvider : IBillingProvider
{
    public Task<string> CreateCustomerAsync(
        Guid firmId, string firmName, string? email, CancellationToken cancellationToken = default) =>
        throw new BillingUnavailableException();

    public Task<Uri> CreateCheckoutAsync(
        string customerId, Guid firmId, Plan plan, CancellationToken cancellationToken = default) =>
        throw new BillingUnavailableException();

    public Task<Uri> CreatePortalAsync(string customerId, CancellationToken cancellationToken = default) =>
        throw new BillingUnavailableException();

    public Task<PaidSubscriptionState?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default) =>
        throw new BillingUnavailableException();

    public BillingEvent? ReadEvent(string payload, string? signature) => throw new BillingUnavailableException();
}
