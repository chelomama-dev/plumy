using Plumy.Application.Firms.AutoApproval;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Tests.Firms;

public class AutoApprovalHandlerTests
{
    private readonly InMemoryFirmStore _store = new();
    private readonly Firm _firm;

    public AutoApprovalHandlerTests() => _firm = _store.Seed("Gestoría López", Guid.NewGuid(), UserProfile.Empty);

    private GetAutoApprovalHandler Get() => new(new FakeTenant(_firm.Id), _store);

    private SetAutoApprovalHandler Set() => new(new FakeTenant(_firm.Id), _store, _store);

    [Fact]
    public async Task Get_ANewFirm_HasItOff()
    {
        Assert.Equal(new AutoApprovalResponse(false, 0.95), await Get().HandleAsync());
    }

    [Fact]
    public async Task Set_SavesTheFirmsChoice()
    {
        var savesBefore = _store.SaveCount;

        var result = await Set().HandleAsync(new SetAutoApprovalCommand(true, 0.9));

        Assert.Equal(new AutoApprovalResponse(true, 0.9), result);
        Assert.Equal(result, await Get().HandleAsync());
        Assert.Equal(savesBefore + 1, _store.SaveCount);
    }

    [Fact]
    public async Task Set_AConfidenceOutOfRange_IsRefused()
    {
        await Assert.ThrowsAsync<DomainException>(() => Set().HandleAsync(new SetAutoApprovalCommand(true, 0.5)));
        Assert.False(_firm.AutoApproval.Enabled);
    }
}
