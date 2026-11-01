using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Billing;
using Plumy.Application.Common;
using Plumy.Application.Tests.Firms;
using Plumy.Domain.Billing;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Tests.Billing;

public class BillingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryFirmStore _store = new();
    private readonly FakeBillingProvider _billing = new();
    private readonly Firm _firm;

    public BillingHandlerTests() => _firm = _store.Seed("Gestoría López", Guid.NewGuid());

    private StartCheckoutHandler Checkout() =>
        new(new FakeTenant(_firm.Id), new FakeCurrentUser(), _store, _store, _billing);

    private OpenBillingPortalHandler Portal() => new(new FakeTenant(_firm.Id), _store, _billing);

    private SyncSubscriptionHandler Sync() =>
        new(_billing, _store, _store, NullLogger<SyncSubscriptionHandler>.Instance);

    private static FirmSubscription Paid(
        string id = "sub_1", Plan plan = Plan.Basic, SubscriptionStatus status = SubscriptionStatus.Active) =>
        FirmSubscription.Paid(id, plan, status, Now, Now.AddMonths(1));

    [Fact]
    public async Task Checkout_TheFirstTime_CreatesTheCustomerAndSavesIt()
    {
        var response = await Checkout().HandleAsync(new StartCheckoutCommand("Pro"));

        Assert.Equal("cus_1", _firm.StripeCustomerId);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(("ana@gestorialopez.es", "Gestoría López"), (_billing.Customers[0].Email, _billing.Customers[0].Name));
        Assert.Equal(new Uri("https://checkout.stripe.test/cus_1/Pro"), response.Url);
        // Nothing changes in Plumy until the webhook says so
        Assert.Equal(Plan.Trial, _firm.Subscription.Plan);
    }

    [Fact]
    public async Task Checkout_Again_ReusesTheCustomer()
    {
        await Checkout().HandleAsync(new StartCheckoutCommand("Basic"));

        await Checkout().HandleAsync(new StartCheckoutCommand("Pro"));

        Assert.Single(_billing.Customers);
        Assert.Equal(1, _store.SaveCount);
    }

    [Theory]
    [InlineData("Trial")]
    [InlineData("basic")]
    [InlineData("Gold")]
    [InlineData("")]
    public async Task Checkout_NotAPaidPlan_IsRefused(string plan)
    {
        await Assert.ThrowsAsync<DomainException>(() => Checkout().HandleAsync(new StartCheckoutCommand(plan)));
        Assert.Empty(_billing.Customers);
    }

    [Fact]
    public async Task Checkout_WhileAlreadyPaying_IsAConflict()
    {
        _firm.LinkStripeCustomer("cus_1");
        _firm.ApplyPaidSubscription(Paid());

        await Assert.ThrowsAsync<ConflictException>(() => Checkout().HandleAsync(new StartCheckoutCommand("Pro")));
    }

    [Fact]
    public async Task Checkout_AfterCanceling_IsAllowed()
    {
        _firm.LinkStripeCustomer("cus_1");
        _firm.ApplyPaidSubscription(Paid(status: SubscriptionStatus.Canceled));

        var response = await Checkout().HandleAsync(new StartCheckoutCommand("Basic"));

        Assert.Equal(new Uri("https://checkout.stripe.test/cus_1/Basic"), response.Url);
    }

    [Fact]
    public async Task Portal_ForAFirmThatNeverPaid_IsAConflict() =>
        await Assert.ThrowsAsync<ConflictException>(() => Portal().HandleAsync());

    [Fact]
    public async Task Portal_OpensForTheFirmsCustomer()
    {
        _firm.LinkStripeCustomer("cus_7");

        Assert.Equal(new Uri("https://billing.stripe.test/cus_7"), (await Portal().HandleAsync()).Url);
    }

    [Fact]
    public async Task Sync_AppliesTheSubscriptionAsStripeHasItNow()
    {
        _firm.LinkStripeCustomer("cus_1");
        _billing.Subscriptions["sub_1"] = new PaidSubscriptionState("cus_1", Paid(plan: Plan.Pro));

        await Sync().HandleAsync(new BillingEvent("evt_1", "customer.subscription.created", "sub_1"));

        Assert.Equal(Plan.Pro, _firm.Subscription.Plan);
        Assert.Equal(SubscriptionStatus.Active, _firm.Subscription.Status);
        Assert.Equal(Now, _firm.Subscription.PeriodStart);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Sync_TheSameEventTwice_EndsTheSame()
    {
        _firm.LinkStripeCustomer("cus_1");
        _billing.Subscriptions["sub_1"] = new PaidSubscriptionState("cus_1", Paid());
        var billingEvent = new BillingEvent("evt_1", "customer.subscription.updated", "sub_1");

        await Sync().HandleAsync(billingEvent);
        var once = _firm.Subscription;
        await Sync().HandleAsync(billingEvent);

        Assert.Equal(once, _firm.Subscription);
    }

    [Fact]
    public async Task Sync_ASubscriptionNotPaidYet_ChangesNothing()
    {
        _firm.LinkStripeCustomer("cus_1");

        await Sync().HandleAsync(new BillingEvent("evt_1", "customer.subscription.created", "sub_unpaid"));

        Assert.Equal(Plan.Trial, _firm.Subscription.Plan);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Sync_ACustomerNoFirmHas_IsIgnored()
    {
        _billing.Subscriptions["sub_1"] = new PaidSubscriptionState("cus_other", Paid());

        await Sync().HandleAsync(new BillingEvent("evt_1", "customer.subscription.created", "sub_1"));

        Assert.Equal(Plan.Trial, _firm.Subscription.Plan);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Sync_AnOldSubscriptionEnding_IsIgnored()
    {
        _firm.LinkStripeCustomer("cus_1");
        _firm.ApplyPaidSubscription(Paid("sub_new", Plan.Pro));
        _billing.Subscriptions["sub_old"] = new PaidSubscriptionState("cus_1", Paid("sub_old", status: SubscriptionStatus.Canceled));

        await Sync().HandleAsync(new BillingEvent("evt_1", "customer.subscription.deleted", "sub_old"));

        Assert.Equal("sub_new", _firm.Subscription.StripeSubscriptionId);
        Assert.Equal(0, _store.SaveCount);
    }
}

/// <summary>Stripe as the handlers see it: deterministic URLs, customers numbered in order, subscriptions set by hand.</summary>
internal sealed class FakeBillingProvider : IBillingProvider
{
    public List<(Guid FirmId, string Name, string? Email)> Customers { get; } = [];

    public Dictionary<string, PaidSubscriptionState> Subscriptions { get; } = [];

    public Task<string> CreateCustomerAsync(
        Guid firmId, string firmName, string? email, CancellationToken cancellationToken = default)
    {
        Customers.Add((firmId, firmName, email));
        return Task.FromResult($"cus_{Customers.Count}");
    }

    public Task<Uri> CreateCheckoutAsync(
        string customerId, Guid firmId, Plan plan, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"https://checkout.stripe.test/{customerId}/{plan}"));

    public Task<Uri> CreatePortalAsync(string customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"https://billing.stripe.test/{customerId}"));

    public Task<PaidSubscriptionState?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Subscriptions.GetValueOrDefault(subscriptionId));

    public BillingEvent? ReadEvent(string payload, string? signature) => throw new NotSupportedException();
}
