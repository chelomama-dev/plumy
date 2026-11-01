using Plumy.Application.Common;
using Plumy.Domain.Billing;
using Plumy.Domain.Firms;
using Plumy.Infrastructure.Persistence.Repositories;

namespace Plumy.Infrastructure.Tests.Persistence;

public class BillingPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private async Task<Firm> AddFirmAsync(string? customerId = null)
    {
        var firm = Firm.Create("Gestoría Ruiz", Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (customerId is not null)
            firm.LinkStripeCustomer(customerId);

        await using var db = fixture.CreateDbContext(firm.Id);
        db.Firms.Add(firm);
        await db.SaveChangesAsync();
        return firm;
    }

    [Fact]
    public async Task A_webhook_finds_the_firm_by_its_customer_and_stores_the_paid_plan()
    {
        var customerId = $"cus_{Guid.NewGuid():N}";
        var firm = await AddFirmAsync(customerId);
        var paid = FirmSubscription.Paid("sub_1", Plan.Pro, SubscriptionStatus.PastDue, Start, Start.AddMonths(1));

        // A webhook has no tenant: any context finds the firm
        await using (var db = fixture.CreateDbContext(Guid.NewGuid()))
        {
            var found = await new FirmRepository(db).GetByStripeCustomerIdAsync(customerId);
            Assert.Equal(firm.Id, found!.Id);
            found.ApplyPaidSubscription(paid);
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(firm.Id);
        Assert.Equal(paid, await new FirmRepository(read).GetSubscriptionAsync(firm.Id));
    }

    [Fact]
    public async Task A_customer_no_firm_has_finds_nothing()
    {
        await using var db = fixture.CreateDbContext();

        Assert.Null(await new FirmRepository(db).GetByStripeCustomerIdAsync("cus_nobody"));
    }

    [Fact]
    public async Task Two_firms_cant_share_a_customer()
    {
        var customerId = $"cus_{Guid.NewGuid():N}";
        await AddFirmAsync(customerId);

        await Assert.ThrowsAsync<ConflictException>(() => AddFirmAsync(customerId));
    }

    [Fact]
    public async Task Many_firms_without_a_customer_are_fine()
    {
        await AddFirmAsync();
        await AddFirmAsync();
    }
}
