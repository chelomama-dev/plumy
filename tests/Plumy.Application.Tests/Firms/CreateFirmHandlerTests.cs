using Plumy.Application.Common;
using Plumy.Application.Firms.CreateFirm;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Tests.Firms;

public class CreateFirmHandlerTests
{
    private readonly InMemoryFirmStore _store = new();
    private readonly FakeCurrentUser _user = new();
    private readonly CreateFirmHandler _handler;

    public CreateFirmHandlerTests() => _handler = new CreateFirmHandler(_user, _store, _store);

    [Fact]
    public async Task HandleAsync_UserWithoutFirm_CreatesFirmWithThemAsOwner()
    {
        var response = await _handler.HandleAsync(new CreateFirmCommand("Gestoría López"));

        var firm = Assert.Single(_store.Saved);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(firm.Id, response.Id);
        Assert.Equal("Gestoría López", response.Name);
        Assert.Equal("Owner", response.Role);

        var owner = Assert.Single(firm.Users);
        Assert.Equal(_user.ObjectId, owner.ObjectId);
        Assert.Equal(FirmRole.Owner, owner.Role);
        Assert.Equal("Ana López", owner.Name);
        Assert.Equal("ana@gestorialopez.es", owner.Email?.Value);
    }

    [Fact]
    public async Task HandleAsync_UserAlreadyInAFirm_ThrowsConflictAndDoesNotSave()
    {
        await _handler.HandleAsync(new CreateFirmCommand("Gestoría López"));

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(new CreateFirmCommand("Otra gestoría")));

        Assert.Single(_store.Saved);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidName_ThrowsAndDoesNotSave()
    {
        await Assert.ThrowsAsync<DomainException>(() => _handler.HandleAsync(new CreateFirmCommand("  ")));

        Assert.Empty(_store.Saved);
        Assert.Equal(0, _store.SaveCount);
    }
}
