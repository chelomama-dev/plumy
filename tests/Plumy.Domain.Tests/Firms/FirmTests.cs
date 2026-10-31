using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Domain.Tests.Firms;

public class FirmTests
{
    private static readonly Guid OwnerObjectId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_SetsProperties()
    {
        var firm = Firm.Create("  Gestoría López  ", OwnerObjectId, DateTimeOffset.UtcNow);

        Assert.NotEqual(Guid.Empty, firm.Id);
        Assert.Equal("Gestoría López", firm.Name);
    }

    [Fact]
    public void Create_MakesTheCreatorItsOwner()
    {
        var firm = Firm.Create("Gestoría López", OwnerObjectId, DateTimeOffset.UtcNow);

        var owner = Assert.Single(firm.Users);
        Assert.Equal(firm.Id, owner.FirmId);
        Assert.Equal(OwnerObjectId, owner.ObjectId);
        Assert.Equal(FirmRole.Owner, owner.Role);
        Assert.NotEqual(Guid.Empty, owner.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Throws(string? name)
    {
        Assert.Throws<DomainException>(() => Firm.Create(name, OwnerObjectId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithTooLongName_Throws()
    {
        var name = new string('a', Firm.MaxNameLength + 1);

        Assert.Throws<DomainException>(() => Firm.Create(name, OwnerObjectId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithNameAtMaxLength_Succeeds()
    {
        var name = new string('a', Firm.MaxNameLength);

        Assert.Equal(name, Firm.Create(name, OwnerObjectId, DateTimeOffset.UtcNow).Name);
    }

    [Fact]
    public void Create_WithoutOwner_Throws()
    {
        Assert.Throws<DomainException>(() => Firm.Create("Gestoría López", Guid.Empty, DateTimeOffset.UtcNow));
    }
}
