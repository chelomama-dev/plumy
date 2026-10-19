using System.Net;
using System.Net.Http.Json;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.Firms;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Api.Tests.Firms;

[Collection(ApiCollection.Name)]
public class FirmEndpointsTests(PlumyApiFactory factory)
{
    [Fact]
    public async Task GetMe_UserWithoutFirm_ReturnsProfileWithoutFirm()
    {
        var me = await factory.CreateClientWithoutFirm().GetFromJsonAsync<CurrentUserResponse>("/api/me");

        Assert.NotNull(me);
        Assert.Equal("Test User", me.Name);
        Assert.Equal("test.user@example.com", me.Email);
        Assert.Null(me.Firm);
    }

    [Fact]
    public async Task PostFirm_UserWithoutFirm_Returns201AndMakesThemOwner()
    {
        var client = factory.CreateClientWithoutFirm();

        var response = await client.PostAsJsonAsync("/api/firms", new { name = "  Gestoría López  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/me", response.Headers.Location?.ToString());
        var firm = await response.Content.ReadFromJsonAsync<FirmMembershipResponse>();
        Assert.NotNull(firm);
        Assert.Equal("Gestoría López", firm.Name);
        Assert.Equal("Owner", firm.Role);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/me");
        Assert.Equal(firm, me?.Firm);
    }

    [Fact]
    public async Task PostFirm_UserAlreadyInAFirm_Returns409()
    {
        var client = await factory.CreateClientInNewFirmAsync();

        var response = await client.PostAsJsonAsync("/api/firms", new { name = "Otra gestoría" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task PostFirm_WithoutName_Returns400(string? name)
    {
        var response = await factory.CreateClientWithoutFirm().PostAsJsonAsync("/api/firms", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FirmEndpoints_WithoutToken_Return401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/firms", new { name = "Gestoría" })).StatusCode);
    }
}
