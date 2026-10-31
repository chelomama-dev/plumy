using Plumy.Application.Billing;
using Plumy.Application.Tests.ClientCompanies;
using Plumy.Application.Tests.Firms;
using Plumy.Application.Tests.Invoices;
using Plumy.Domain.Billing;
using Plumy.Domain.Invoices;

namespace Plumy.Application.Tests.Billing;

public class GetSubscriptionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryFirmStore _firms = new();
    private readonly InMemoryInvoiceStore _invoices = new();

    private GetSubscriptionHandler Handler(DateTimeOffset now)
    {
        var tenant = new TestTenant();
        return new GetSubscriptionHandler(new SubscriptionUsage(tenant, _firms, _invoices), new FixedTimeProvider(now));
    }

    private async Task UploadAtAsync(DateTimeOffset at)
    {
        _invoices.Add(Invoice.Create(
            TestTenant.FirmId, Guid.NewGuid(), "factura.pdf", InvoiceFileFormat.Pdf, 100, new string('a', 64),
            Guid.NewGuid(), at));
        await _invoices.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_CountsOnlyTheUploadsOfThePeriod()
    {
        var trialStart = Now.AddDays(-3);
        _firms.Subscriptions[TestTenant.FirmId] = FirmSubscription.StartTrial(trialStart);
        await UploadAtAsync(trialStart.AddMinutes(-1));
        await UploadAtAsync(trialStart);
        await UploadAtAsync(Now);

        var response = await Handler(Now).HandleAsync();

        Assert.Equal("Trial", response.Plan);
        Assert.Equal("Trialing", response.Status);
        Assert.Equal(trialStart, response.PeriodStart);
        Assert.Equal(trialStart.AddDays(14), response.PeriodEnd);
        Assert.Equal(50, response.InvoiceLimit);
        Assert.Equal(2, response.UploadedInPeriod);
        Assert.Equal("None", response.UploadBlock);
    }

    [Fact]
    public async Task HandleAsync_TrialOver_SaysWhy()
    {
        _firms.Subscriptions[TestTenant.FirmId] = FirmSubscription.StartTrial(Now.AddDays(-20));

        Assert.Equal("TrialEnded", (await Handler(Now).HandleAsync()).UploadBlock);
    }

    [Fact]
    public async Task HandleAsync_NoFirm_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(Now).HandleAsync());
    }
}
