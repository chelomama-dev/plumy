namespace Plumy.Worker.Tests;

public class MessageTenantContextTests
{
    [Fact]
    public void FirmId_BeforeSet_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new MessageTenantContext().FirmId);
    }

    [Fact]
    public void Set_GivesTheFirm_AndSettingItAgainIsHarmless()
    {
        var firmId = Guid.NewGuid();
        var tenant = new MessageTenantContext();

        tenant.Set(firmId);
        tenant.Set(firmId);

        Assert.Equal(firmId, tenant.FirmId);
    }

    [Fact]
    public void Set_AnotherFirm_Throws()
    {
        var tenant = new MessageTenantContext();
        tenant.Set(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => tenant.Set(Guid.NewGuid()));
    }

    [Fact]
    public void Set_EmptyFirm_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MessageTenantContext().Set(Guid.Empty));
    }
}
