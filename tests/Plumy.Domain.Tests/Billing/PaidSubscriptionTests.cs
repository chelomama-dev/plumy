using Plumy.Domain.Billing;
using Plumy.Domain.Firms;

namespace Plumy.Domain.Tests.Billing;

public class PaidSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static FirmSubscription Paid(
        string id = "sub_1", Plan plan = Plan.Basic, SubscriptionStatus status = SubscriptionStatus.Active) =>
        FirmSubscription.Paid(id, plan, status, Now, Now.AddMonths(1));

    private static Firm NewFirm() => Firm.Create("Gestoría López", Guid.NewGuid(), Now);

    [Fact]
    public void Paid_KeepsWhatStripeSays()
    {
        var subscription = Paid(plan: Plan.Pro, status: SubscriptionStatus.PastDue);

        Assert.Equal("sub_1", subscription.StripeSubscriptionId);
        Assert.Equal(Plan.Pro, subscription.Plan);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(1500, subscription.InvoiceLimit);
        Assert.True(subscription.IsPaying);
    }

    [Fact]
    public void Paid_TheTrialPlan_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Paid(plan: Plan.Trial));

    [Fact]
    public void Paid_TrialingStatus_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Paid(status: SubscriptionStatus.Trialing));

    [Fact]
    public void Paid_APeriodThatEndsBeforeItStarts_IsRefused() =>
        Assert.Throws<ArgumentException>(() =>
            FirmSubscription.Paid("sub_1", Plan.Basic, SubscriptionStatus.Active, Now, Now));

    [Fact]
    public void IsPaying_NotForTheTrialNorForACanceledPlan()
    {
        Assert.False(FirmSubscription.StartTrial(Now).IsPaying);
        Assert.False(Paid(status: SubscriptionStatus.Canceled).IsPaying);
    }

    [Fact]
    public void LinkStripeCustomer_TheSameOneTwice_IsHarmless()
    {
        var firm = NewFirm();

        firm.LinkStripeCustomer("cus_1");
        firm.LinkStripeCustomer("cus_1");

        Assert.Equal("cus_1", firm.StripeCustomerId);
    }

    [Fact]
    public void LinkStripeCustomer_ADifferentOne_Throws()
    {
        var firm = NewFirm();
        firm.LinkStripeCustomer("cus_1");

        Assert.Throws<InvalidOperationException>(() => firm.LinkStripeCustomer("cus_2"));
    }

    [Fact]
    public void ApplyPaidSubscription_ReplacesTheTrial()
    {
        var firm = NewFirm();

        Assert.True(firm.ApplyPaidSubscription(Paid()));

        Assert.Equal(Plan.Basic, firm.Subscription.Plan);
        Assert.Equal(SubscriptionStatus.Active, firm.Subscription.Status);
    }

    [Fact]
    public void ApplyPaidSubscription_ChangesToTheSameSubscription_AreApplied()
    {
        var firm = NewFirm();
        firm.ApplyPaidSubscription(Paid());

        Assert.True(firm.ApplyPaidSubscription(Paid(status: SubscriptionStatus.Canceled)));

        Assert.Equal(SubscriptionStatus.Canceled, firm.Subscription.Status);
    }

    [Fact]
    public void ApplyPaidSubscription_AnOldSubscriptionEnding_DoesntCancelTheCurrentOne()
    {
        var firm = NewFirm();
        firm.ApplyPaidSubscription(Paid("sub_new", Plan.Pro));

        Assert.False(firm.ApplyPaidSubscription(Paid("sub_old", status: SubscriptionStatus.Canceled)));

        Assert.Equal("sub_new", firm.Subscription.StripeSubscriptionId);
        Assert.Equal(Plan.Pro, firm.Subscription.Plan);
    }

    [Fact]
    public void ApplyPaidSubscription_ANewSubscriptionAfterCanceling_IsApplied()
    {
        var firm = NewFirm();
        firm.ApplyPaidSubscription(Paid("sub_old", status: SubscriptionStatus.Canceled));

        Assert.True(firm.ApplyPaidSubscription(Paid("sub_new")));

        Assert.Equal("sub_new", firm.Subscription.StripeSubscriptionId);
    }

    [Fact]
    public void ApplyPaidSubscription_TheTrial_Throws() =>
        Assert.Throws<ArgumentException>(() => NewFirm().ApplyPaidSubscription(FirmSubscription.StartTrial(Now)));
}
