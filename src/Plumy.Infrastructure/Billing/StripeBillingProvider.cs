using Plumy.Application.Billing;
using Plumy.Domain.Billing;
using Stripe;
using Stripe.Checkout;
using Plan = Plumy.Domain.Billing.Plan;

namespace Plumy.Infrastructure.Billing;

/// <summary>Stripe's settings, from the "Stripe" section. The two keys are secrets: user-secrets locally, Key Vault in Azure.</summary>
/// <param name="SecretKey">A test-mode secret or restricted key (sk_test_/rk_test_).</param>
/// <param name="WebhookSecret">Signs the webhooks (whsec_). Locally, the one <c>stripe listen</c> prints.</param>
/// <param name="ReturnUrl">The billing page of the web app, where Stripe sends people back.</param>
internal sealed record StripeOptions(string SecretKey, string WebhookSecret, Uri ReturnUrl);

/// <summary>
/// Stripe-hosted pages for everything with a card (Checkout, Customer Portal) and the webhooks that say what happened.
/// The prices are found by lookup key, so a new price in Stripe needs no deploy: move the key to it.
/// </summary>
internal sealed class StripeBillingProvider(IStripeClient client, StripeOptions options) : IBillingProvider
{
    public const string BasicLookupKey = "plumy_basic_monthly";
    public const string ProLookupKey = "plumy_pro_monthly";

    // Stripe doesn't hold back events whose API version differs from the library's, and Plumy only reads ids from
    // them (the subscription itself is fetched again), so a mismatch is harmless
    private const bool ThrowOnApiVersionMismatch = false;

    public async Task<string> CreateCustomerAsync(
        Guid firmId, string firmName, string? email, CancellationToken cancellationToken = default)
    {
        var customer = await new CustomerService(client).CreateAsync(
            new CustomerCreateOptions
            {
                Name = firmName,
                Email = email,
                PreferredLocales = ["es"],
                Metadata = new Dictionary<string, string> { ["firmId"] = firmId.ToString() }
            },
            // Two clicks racing get the same customer: Stripe replays the first answer for 24 hours
            new RequestOptions { IdempotencyKey = $"customer-{firmId}" },
            cancellationToken);

        return customer.Id;
    }

    public async Task<Uri> CreateCheckoutAsync(
        string customerId, Guid firmId, Plan plan, CancellationToken cancellationToken = default)
    {
        var session = await new SessionService(client).CreateAsync(
            new SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                ClientReferenceId = firmId.ToString(),
                LineItems = [new SessionLineItemOptions { Price = await FindPriceAsync(plan, cancellationToken), Quantity = 1 }],
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { ["firmId"] = firmId.ToString() }
                },
                Locale = "es",
                // The screen only looks again on the way back: the webhook is what changes the plan
                SuccessUrl = WithQuery(options.ReturnUrl, "checkout=done"),
                CancelUrl = options.ReturnUrl.ToString()
            },
            cancellationToken: cancellationToken);

        return new Uri(session.Url);
    }

    public async Task<Uri> CreatePortalAsync(string customerId, CancellationToken cancellationToken = default)
    {
        // Uses the portal configuration saved in Stripe's dashboard (Settings > Billing > Customer portal)
        var session = await new Stripe.BillingPortal.SessionService(client).CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = options.ReturnUrl.ToString(),
                Locale = "es"
            },
            cancellationToken: cancellationToken);

        return new Uri(session.Url);
    }

    public async Task<PaidSubscriptionState?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await new SubscriptionService(client).GetAsync(
            subscriptionId, cancellationToken: cancellationToken);

        return StripeSubscriptions.Map(subscription);
    }

    public BillingEvent? ReadEvent(string payload, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            throw new InvalidBillingEventException("The Stripe-Signature header is missing.");

        Event stripeEvent;
        try
        {
            // Also refuses events signed more than five minutes ago: a replayed request
            stripeEvent = EventUtility.ConstructEvent(
                payload, signature, options.WebhookSecret, throwOnApiVersionMismatch: ThrowOnApiVersionMismatch);
        }
        catch (StripeException ex)
        {
            throw new InvalidBillingEventException("The webhook's signature isn't valid.", ex);
        }

        var subscriptionId = stripeEvent.Data.Object switch
        {
            // Only Checkout in subscription mode creates one
            Session { SubscriptionId: { } id } when stripeEvent.Type == EventTypes.CheckoutSessionCompleted => id,
            Subscription subscription when stripeEvent.Type.StartsWith("customer.subscription.", StringComparison.Ordinal)
                => subscription.Id,
            _ => null
        };

        return subscriptionId is null ? null : new BillingEvent(stripeEvent.Id, stripeEvent.Type, subscriptionId);
    }

    private async Task<string> FindPriceAsync(Plan plan, CancellationToken cancellationToken)
    {
        var lookupKey = plan switch
        {
            Plan.Basic => BasicLookupKey,
            Plan.Pro => ProLookupKey,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Only paid plans have a price.")
        };

        var prices = await new PriceService(client).ListAsync(
            new PriceListOptions { LookupKeys = [lookupKey], Active = true }, cancellationToken: cancellationToken);

        return prices.Data.SingleOrDefault()?.Id
            ?? throw new InvalidOperationException($"Stripe has no active price with lookup key '{lookupKey}'.");
    }

    private static string WithQuery(Uri url, string query) =>
        new UriBuilder(url) { Query = string.IsNullOrEmpty(url.Query) ? query : $"{url.Query.TrimStart('?')}&{query}" }
            .Uri.ToString();
}

/// <summary>Stripe's subscriptions in Plumy's terms (ADR 0012), so nothing past this file sees Stripe's statuses.</summary>
internal static class StripeSubscriptions
{
    public static PaidSubscriptionState? Map(Subscription subscription)
    {
        var status = subscription.Status switch
        {
            // Plumy runs the trial itself, so a Stripe trial (set up by hand in the dashboard) counts as paid
            "active" or "trialing" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" or "unpaid" or "paused" => SubscriptionStatus.Canceled,
            // The first payment hasn't gone through (or never did): the firm stays where it was
            "incomplete" or "incomplete_expired" => (SubscriptionStatus?)null,
            var other => throw new InvalidOperationException($"Unknown Stripe subscription status '{other}'.")
        };
        if (status is null)
            return null;

        // One item per subscription: the plan. Since API 2025-03-31 the billing period lives on the item
        var item = subscription.Items.Data.Single();
        var plan = item.Price.LookupKey switch
        {
            StripeBillingProvider.BasicLookupKey => Plan.Basic,
            StripeBillingProvider.ProLookupKey => Plan.Pro,
            var other => throw new InvalidOperationException(
                $"Subscription {subscription.Id} has price {item.Price.Id} with lookup key '{other}', which is no plan.")
        };

        return new PaidSubscriptionState(
            subscription.CustomerId,
            FirmSubscription.Paid(
                subscription.Id, plan, status.Value, Utc(item.CurrentPeriodStart), Utc(item.CurrentPeriodEnd)));
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
