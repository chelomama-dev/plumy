using System.Collections.Concurrent;
using Plumy.Application.Billing;
using Plumy.Domain.Billing;

namespace Plumy.Api.Tests.Infrastructure;

/// <summary>
/// Stripe for the API tests. Its webhooks are plain: the body is the subscription id, and the signature is valid when
/// it is <see cref="ValidSignature"/>. Stripe's real signing is tested in Plumy.Infrastructure.Tests.
/// </summary>
public sealed class FakeBillingProvider : IBillingProvider
{
    public const string ValidSignature = "t=1,v1=valid";

    /// <summary>The subscriptions as Stripe has them, by id. Tests set them before sending the event.</summary>
    public ConcurrentDictionary<string, PaidSubscriptionState> Subscriptions { get; } = [];

    // Shared by every test class: unique ids, so firms never share a customer
    public Task<string> CreateCustomerAsync(
        Guid firmId, string firmName, string? email, CancellationToken cancellationToken = default) =>
        Task.FromResult($"cus_{firmId:N}");

    public Task<Uri> CreateCheckoutAsync(
        string customerId, Guid firmId, Plan plan, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"https://checkout.stripe.test/{customerId}/{plan}"));

    public Task<Uri> CreatePortalAsync(string customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"https://billing.stripe.test/{customerId}"));

    public Task<PaidSubscriptionState?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Subscriptions.GetValueOrDefault(subscriptionId));

    public BillingEvent? ReadEvent(string payload, string? signature) =>
        signature == ValidSignature
            ? new BillingEvent($"evt_{Guid.NewGuid():N}", "customer.subscription.updated", payload)
            : throw new InvalidBillingEventException("The webhook's signature isn't valid.");
}
