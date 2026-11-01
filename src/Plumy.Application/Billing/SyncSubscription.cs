using Microsoft.Extensions.Logging;
using Plumy.Application.Abstractions;
using Plumy.Application.Firms;

namespace Plumy.Application.Billing;

/// <summary>
/// Brings a firm's plan up to date after one of Stripe's events (ADR 0012: the webhooks are the source of truth).
/// It reads the subscription from Stripe instead of the event, so the result is the same however many times an event
/// comes, and in whatever order: that is what makes the webhook idempotent. Runs without a signed-in user or tenant.
/// </summary>
public sealed partial class SyncSubscriptionHandler(
    IBillingProvider billing, IFirmRepository firms, IUnitOfWork unitOfWork, ILogger<SyncSubscriptionHandler> logger)
{
    public async Task HandleAsync(BillingEvent billingEvent, CancellationToken cancellationToken = default)
    {
        var state = await billing.GetSubscriptionAsync(billingEvent.SubscriptionId, cancellationToken);
        if (state is null)
        {
            LogNotYet(billingEvent.Id, billingEvent.SubscriptionId);
            return;
        }

        // Plumy creates every customer, so one it doesn't know was made in Stripe's dashboard or by another app
        var firm = await firms.GetByStripeCustomerIdAsync(state.CustomerId, cancellationToken);
        if (firm is null)
        {
            LogUnknownCustomer(billingEvent.Id, state.CustomerId);
            return;
        }

        if (!firm.ApplyPaidSubscription(state.Subscription))
        {
            LogStale(billingEvent.Id, billingEvent.SubscriptionId, firm.Id);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogApplied(firm.Id, state.Subscription.Plan.ToString(), state.Subscription.Status.ToString(), billingEvent.Id);
    }

    [LoggerMessage(LogLevel.Information, "Event {EventId}: subscription {SubscriptionId} isn't paid yet, nothing to do")]
    private partial void LogNotYet(string eventId, string subscriptionId);

    [LoggerMessage(LogLevel.Warning, "Event {EventId}: no firm is Stripe customer {CustomerId}")]
    private partial void LogUnknownCustomer(string eventId, string customerId);

    [LoggerMessage(LogLevel.Information,
        "Event {EventId}: subscription {SubscriptionId} ended, but firm {FirmId} pays another one; ignored")]
    private partial void LogStale(string eventId, string subscriptionId, Guid firmId);

    [LoggerMessage(LogLevel.Information, "Firm {FirmId} is now on {Plan} ({Status}) after event {EventId}")]
    private partial void LogApplied(Guid firmId, string plan, string status, string eventId);
}
