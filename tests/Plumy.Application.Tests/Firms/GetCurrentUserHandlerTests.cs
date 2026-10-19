using Plumy.Application.Firms.CreateFirm;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Application.Tests.Firms;

public class GetCurrentUserHandlerTests
{
    private readonly InMemoryFirmStore _store = new();
    private readonly FakeCurrentUser _user = new();

    [Fact]
    public async Task HandleAsync_UserWithoutFirm_ReturnsProfileWithoutFirm()
    {
        var response = await new GetCurrentUserHandler(_user, _store).HandleAsync();

        Assert.Equal("Ana López", response.Name);
        Assert.Equal("ana@gestorialopez.es", response.Email);
        Assert.Null(response.Firm);
    }

    [Fact]
    public async Task HandleAsync_UserInAFirm_ReturnsTheirFirmAndRole()
    {
        var created = await new CreateFirmHandler(_user, _store, _store)
            .HandleAsync(new CreateFirmCommand("Gestoría López"));

        var response = await new GetCurrentUserHandler(_user, _store).HandleAsync();

        Assert.Equal(created, response.Firm);
    }

    [Fact]
    public async Task HandleAsync_AnotherUsersFirm_IsNotReturned()
    {
        await new CreateFirmHandler(new FakeCurrentUser(), _store, _store)
            .HandleAsync(new CreateFirmCommand("Gestoría de otro"));

        var response = await new GetCurrentUserHandler(_user, _store).HandleAsync();

        Assert.Null(response.Firm);
    }
}
