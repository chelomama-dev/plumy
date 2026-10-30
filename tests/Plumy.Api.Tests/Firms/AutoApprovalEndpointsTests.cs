using System.Net;
using System.Net.Http.Json;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.Firms;
using Plumy.Application.Firms.AutoApproval;

namespace Plumy.Api.Tests.Firms;

[Collection(ApiCollection.Name)]
public class AutoApprovalEndpointsTests(PlumyApiFactory factory)
{
    private const string Url = "/api/firm/auto-approval";

    private async Task<HttpClient> AddMemberAsync(HttpClient owner)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var invited = await owner.PostAsJsonAsync("/api/firm/invitations", new { email });
        var invitation = await invited.Content.ReadFromJsonAsync<InvitationResponse>();
        var member = factory.CreateClientWithoutFirm(email, "Luis Pérez");
        (await member.PostAsync($"/api/invitations/{invitation!.Id}/accept", null)).EnsureSuccessStatusCode();
        return member;
    }

    [Fact]
    public async Task A_new_firm_has_it_off()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        Assert.Equal(new AutoApprovalResponse(false, 0.95), await owner.GetFromJsonAsync<AutoApprovalResponse>(Url));
    }

    [Fact]
    public async Task The_owner_turns_it_on_and_every_member_sees_it()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var member = await AddMemberAsync(owner);

        var response = await owner.PutAsJsonAsync(Url, new { enabled = true, minConfidence = 0.9 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new AutoApprovalResponse(true, 0.9), await response.Content.ReadFromJsonAsync<AutoApprovalResponse>());
        Assert.Equal(new AutoApprovalResponse(true, 0.9), await member.GetFromJsonAsync<AutoApprovalResponse>(Url));
    }

    [Fact]
    public async Task A_member_cannot_change_it()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var member = await AddMemberAsync(owner);

        var response = await member.PutAsJsonAsync(Url, new { enabled = true, minConfidence = 0.9 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await owner.GetFromJsonAsync<AutoApprovalResponse>(Url))!.Enabled);
    }

    [Fact]
    public async Task A_confidence_out_of_range_is_a_bad_request()
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        var response = await owner.PutAsJsonAsync(Url, new { enabled = true, minConfidence = 0.5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task It_is_each_firms_own()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var other = await factory.CreateClientInNewFirmAsync();

        (await owner.PutAsJsonAsync(Url, new { enabled = true, minConfidence = 0.9 })).EnsureSuccessStatusCode();

        Assert.False((await other.GetFromJsonAsync<AutoApprovalResponse>(Url))!.Enabled);
    }
}
