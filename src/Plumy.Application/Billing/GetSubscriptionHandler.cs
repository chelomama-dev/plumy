using Plumy.Domain.Billing;

namespace Plumy.Application.Billing;

/// <param name="UploadBlock">Why no more invoices can be uploaded, or "None".</param>
public sealed record SubscriptionResponse(
    string Plan, string Status, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd, int InvoiceLimit,
    int UploadedInPeriod, string UploadBlock);

/// <summary>The firm's plan and how much of it is used. Any member can see it.</summary>
public sealed class GetSubscriptionHandler(SubscriptionUsage usage, TimeProvider clock)
{
    public async Task<SubscriptionResponse> HandleAsync(CancellationToken cancellationToken = default)
    {
        var current = await usage.GetAsync(cancellationToken);
        var subscription = current.Subscription;

        return new SubscriptionResponse(
            subscription.Plan.ToString(), subscription.Status.ToString(), subscription.PeriodStart,
            subscription.PeriodEnd, subscription.InvoiceLimit, current.UploadedInPeriod,
            current.CheckUpload(clock.GetUtcNow()).ToString());
    }
}
