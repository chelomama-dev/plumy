using Plumy.Domain.Billing;
using Plumy.Domain.Firms;

namespace Plumy.Domain.Tests.Billing;

public class FirmSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewFirm_StartsAFourteenDayTrial()
    {
        var subscription = Firm.Create("Gestoría López", Guid.NewGuid(), Now).Subscription;

        Assert.Equal(Plan.Trial, subscription.Plan);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Now, subscription.PeriodStart);
        Assert.Equal(Now.AddDays(14), subscription.PeriodEnd);
        Assert.Equal(50, subscription.InvoiceLimit);
    }

    [Fact]
    public void CheckUpload_TrialWithRoom_IsAllowed() =>
        Assert.Equal(UploadBlock.None, FirmSubscription.StartTrial(Now).CheckUpload(Now.AddDays(13), 49));

    [Fact]
    public void CheckUpload_TrialLimitReached_IsBlocked() =>
        Assert.Equal(UploadBlock.LimitReached, FirmSubscription.StartTrial(Now).CheckUpload(Now.AddDays(1), 50));

    [Fact]
    public void CheckUpload_TrialOver_IsBlockedEvenWithRoom() =>
        Assert.Equal(UploadBlock.TrialEnded, FirmSubscription.StartTrial(Now).CheckUpload(Now.AddDays(14), 0));

    [Fact]
    public void CheckUpload_NegativeCount_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FirmSubscription.StartTrial(Now).CheckUpload(Now, -1));

    [Theory]
    [InlineData(Plan.Trial, 50)]
    [InlineData(Plan.Basic, 300)]
    [InlineData(Plan.Pro, 1500)]
    public void InvoiceLimit_GrowsWithThePlan(Plan plan, int limit) => Assert.Equal(limit, plan.InvoiceLimit());
}
