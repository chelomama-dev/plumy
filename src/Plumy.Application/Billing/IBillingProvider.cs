using Plumy.Domain.Billing;

namespace Plumy.Application.Billing;

/// <summary>
/// The payments provider (Stripe, ADR 0012), seen from the use cases. The cards, prices and receipts stay on its side:
/// Plumy only sends people to its pages and listens to what it says happened.
/// </summary>
public interface IBillingProvider
{
    /// <summary>Creates the firm as a customer and returns its id.</summary>
    Task<string> CreateCustomerAsync(
        Guid firmId, string firmName, string? email, CancellationToken cancellationToken = default);

    /// <summary>The page where the owner enters a card and subscribes to the plan.</summary>
    Task<Uri> CreateCheckoutAsync(
        string customerId, Guid firmId, Plan plan, CancellationToken cancellationToken = default);

    /// <summary>The page where the owner changes plan, updates the card, sees the receipts or cancels.</summary>
    Task<Uri> CreatePortalAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The subscription as it is now, or null if it means nothing for Plumy yet (its first payment hasn't gone
    /// through). Asked for on every event instead of trusting the event's copy, which can be old.
    /// </summary>
    Task<PaidSubscriptionState?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the signature of a webhook and says which subscription it is about, or null if it isn't about one.
    /// Throws <see cref="InvalidBillingEventException"/> if it wasn't sent by the provider.
    /// </summary>
    BillingEvent? ReadEvent(string payload, string? signature);
}

/// <param name="Id">The provider's event id, for the logs.</param>
public sealed record BillingEvent(string Id, string Type, string SubscriptionId);

public sealed record PaidSubscriptionState(string CustomerId, FirmSubscription Subscription);

/// <summary>A webhook without a valid signature: someone other than the provider, or a replay. The API answers 400.</summary>
public sealed class InvalidBillingEventException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Payments aren't set up in this environment (no API key). The API answers 503.</summary>
public sealed class BillingUnavailableException()
    : Exception("Payments aren't available right now. Try again later.");
