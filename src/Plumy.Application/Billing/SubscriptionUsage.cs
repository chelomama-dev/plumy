using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Application.Firms;
using Plumy.Application.Invoices;
using Plumy.Domain.Billing;

namespace Plumy.Application.Billing;

/// <summary>The current firm's subscription and how many invoices it has uploaded in the period.</summary>
public sealed record SubscriptionUsageSnapshot(FirmSubscription Subscription, int UploadedInPeriod)
{
    public UploadBlock CheckUpload(DateTimeOffset now) => Subscription.CheckUpload(now, UploadedInPeriod);
}

/// <summary>
/// Reads the plan and counts the uploads, for the upload itself and for the screen that shows them. Every invoice
/// uploaded counts, rejected ones too: reading it already cost what the limit is there to cover.
/// </summary>
public sealed class SubscriptionUsage(ITenantContext tenant, IFirmRepository firms, IInvoiceRepository invoices)
{
    public async Task<SubscriptionUsageSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var subscription = await firms.GetSubscriptionAsync(tenant.FirmId, cancellationToken)
            ?? throw new InvalidOperationException($"The current firm {tenant.FirmId} doesn't exist.");

        var uploaded = await invoices.CountUploadedSinceAsync(subscription.PeriodStart, cancellationToken);
        return new SubscriptionUsageSnapshot(subscription, uploaded);
    }

    /// <summary>
    /// Throws <see cref="PaymentRequiredException"/> if the firm can't upload one more invoice. Two uploads racing
    /// for the last place can both get in: the limit is a few invoices soft, which isn't worth a lock.
    /// </summary>
    public async Task EnsureCanUploadAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var usage = await GetAsync(cancellationToken);

        var message = usage.CheckUpload(now) switch
        {
            UploadBlock.None => null,
            UploadBlock.TrialEnded => "The free trial has ended. Choose a plan to keep uploading invoices.",
            UploadBlock.LimitReached =>
                $"This month's {usage.Subscription.InvoiceLimit} invoices are used up. Change plan to upload more.",
            UploadBlock.NotSubscribed => "The firm has no active plan. Choose one to keep uploading invoices.",
            var block => throw new InvalidOperationException($"Unknown upload block {block}.")
        };

        if (message is not null)
            throw new PaymentRequiredException(message);
    }
}
