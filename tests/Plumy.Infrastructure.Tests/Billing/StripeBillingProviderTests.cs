using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plumy.Application.Billing;
using Plumy.Domain.Billing;
using Plumy.Infrastructure.Billing;
using Stripe;
using Plan = Plumy.Domain.Billing.Plan;

namespace Plumy.Infrastructure.Tests.Billing;

public sealed class StripeBillingProviderTests
{
    private const string WebhookSecret = "whsec_test_secret";

    // No network: reading webhooks only needs the signing secret
    private static readonly StripeBillingProvider Provider = new(
        new StripeClient("sk_test_unused"),
        new StripeOptions("sk_test_unused", WebhookSecret, new Uri("http://localhost:4200/billing")));

    private static string EventJson(string type, string dataObject) => $$"""
        {
          "id": "evt_1",
          "object": "event",
          "api_version": "2024-06-20",
          "created": 1793527200,
          "livemode": false,
          "type": "{{type}}",
          "data": { "object": {{dataObject}} }
        }
        """;

    private static string Sign(string payload, long? timestamp = null) =>
        EventUtility.GenerateSignatureHeader(payload, WebhookSecret, timestamp);

    [Fact]
    public void ReadEvent_ASubscriptionEvent_GivesItsSubscription()
    {
        var payload = EventJson("customer.subscription.updated", """{ "id": "sub_1", "object": "subscription" }""");

        var billingEvent = Provider.ReadEvent(payload, Sign(payload));

        // An older API version than the library's is fine: only the ids are read
        Assert.Equal(new BillingEvent("evt_1", "customer.subscription.updated", "sub_1"), billingEvent);
    }

    [Fact]
    public void ReadEvent_ACompletedCheckout_GivesTheSubscriptionItCreated()
    {
        var payload = EventJson(
            "checkout.session.completed",
            """{ "id": "cs_1", "object": "checkout.session", "mode": "subscription", "subscription": "sub_9" }""");

        Assert.Equal("sub_9", Provider.ReadEvent(payload, Sign(payload))!.SubscriptionId);
    }

    [Fact]
    public void ReadEvent_AnEventAboutSomethingElse_IsNull()
    {
        var payload = EventJson("customer.created", """{ "id": "cus_1", "object": "customer" }""");

        Assert.Null(Provider.ReadEvent(payload, Sign(payload)));
    }

    [Fact]
    public void ReadEvent_ACheckoutWithoutSubscription_IsNull()
    {
        var payload = EventJson(
            "checkout.session.completed", """{ "id": "cs_1", "object": "checkout.session", "mode": "payment" }""");

        Assert.Null(Provider.ReadEvent(payload, Sign(payload)));
    }

    [Fact]
    public void ReadEvent_WithoutSignature_IsRefused()
    {
        var payload = EventJson("customer.subscription.updated", """{ "id": "sub_1", "object": "subscription" }""");

        Assert.Throws<InvalidBillingEventException>(() => Provider.ReadEvent(payload, null));
    }

    [Fact]
    public void ReadEvent_ATamperedPayload_IsRefused()
    {
        var payload = EventJson("customer.subscription.updated", """{ "id": "sub_1", "object": "subscription" }""");
        var signature = Sign(payload);

        Assert.Throws<InvalidBillingEventException>(() =>
            Provider.ReadEvent(payload.Replace("sub_1", "sub_2"), signature));
    }

    [Fact]
    public void ReadEvent_SignedWithAnotherSecret_IsRefused()
    {
        var payload = EventJson("customer.subscription.updated", """{ "id": "sub_1", "object": "subscription" }""");

        Assert.Throws<InvalidBillingEventException>(() =>
            Provider.ReadEvent(payload, EventUtility.GenerateSignatureHeader(payload, "whsec_other", null)));
    }

    [Fact]
    public void ReadEvent_SignedTenMinutesAgo_IsRefusedAsAReplay()
    {
        var payload = EventJson("customer.subscription.updated", """{ "id": "sub_1", "object": "subscription" }""");
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        Assert.Throws<InvalidBillingEventException>(() => Provider.ReadEvent(payload, Sign(payload, tenMinutesAgo)));
    }

    private static readonly DateTime Start = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Subscription StripeSubscription(string status, string lookupKey = StripeBillingProvider.BasicLookupKey) => new()
    {
        Id = "sub_1",
        Status = status,
        CustomerId = "cus_1",
        Items = new StripeList<SubscriptionItem>
        {
            Data =
            [
                new SubscriptionItem
                {
                    Price = new Price { Id = "price_1", LookupKey = lookupKey },
                    CurrentPeriodStart = Start,
                    CurrentPeriodEnd = Start.AddMonths(1)
                }
            ]
        }
    };

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.Canceled)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("paused", SubscriptionStatus.Canceled)]
    public void Map_TranslatesStripesStatus(string stripeStatus, SubscriptionStatus status)
    {
        var state = StripeSubscriptions.Map(StripeSubscription(stripeStatus))!;

        Assert.Equal("cus_1", state.CustomerId);
        Assert.Equal(status, state.Subscription.Status);
        Assert.Equal("sub_1", state.Subscription.StripeSubscriptionId);
        Assert.Equal(new DateTimeOffset(Start), state.Subscription.PeriodStart);
        Assert.Equal(new DateTimeOffset(Start.AddMonths(1)), state.Subscription.PeriodEnd);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("incomplete_expired")]
    public void Map_BeforeTheFirstPayment_IsNull(string stripeStatus) =>
        Assert.Null(StripeSubscriptions.Map(StripeSubscription(stripeStatus)));

    [Theory]
    [InlineData(StripeBillingProvider.BasicLookupKey, Plan.Basic)]
    [InlineData(StripeBillingProvider.ProLookupKey, Plan.Pro)]
    public void Map_FindsThePlanByThePricesLookupKey(string lookupKey, Plan plan) =>
        Assert.Equal(plan, StripeSubscriptions.Map(StripeSubscription("active", lookupKey))!.Subscription.Plan);

    [Fact]
    public void Map_APriceThatIsNoPlan_Throws() =>
        Assert.Throws<InvalidOperationException>(() => StripeSubscriptions.Map(StripeSubscription("active", "other")));

    [Fact]
    public void Map_AnUnknownStatus_Throws() =>
        Assert.Throws<InvalidOperationException>(() => StripeSubscriptions.Map(StripeSubscription("new_status")));

    private static IBillingProvider Resolve(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddBilling();
        return builder.Services.BuildServiceProvider().GetRequiredService<IBillingProvider>();
    }

    private static Dictionary<string, string?> Settings(string secretKey = "sk_test_x") => new()
    {
        ["Stripe:SecretKey"] = secretKey,
        ["Stripe:WebhookSecret"] = WebhookSecret,
        ["Stripe:ReturnUrl"] = "https://app.getplumy.com/billing"
    };

    [Fact]
    public void AddBilling_WithAKey_UsesStripe() => Assert.IsType<StripeBillingProvider>(Resolve(Settings()));

    [Fact]
    public void AddBilling_WithoutAKey_TurnsPaymentsOff() =>
        Assert.IsType<DisabledBillingProvider>(Resolve(new Dictionary<string, string?>()));

    [Theory]
    [InlineData("sk_live_x")]
    [InlineData("rk_live_x")]
    public void AddBilling_ALiveKey_RefusesToStart(string secretKey) =>
        Assert.Throws<InvalidOperationException>(() => Resolve(Settings(secretKey)));

    [Theory]
    [InlineData("Stripe:WebhookSecret", null)]
    [InlineData("Stripe:ReturnUrl", null)]
    [InlineData("Stripe:ReturnUrl", "app.getplumy.com/billing")]
    public void AddBilling_AKeyWithoutTheRest_RefusesToStart(string setting, string? value)
    {
        var settings = Settings();
        settings[setting] = value;

        Assert.Throws<InvalidOperationException>(() => Resolve(settings));
    }
}
