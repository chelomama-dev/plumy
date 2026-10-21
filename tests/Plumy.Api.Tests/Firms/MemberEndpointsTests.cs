using System.Net;
using System.Net.Http.Json;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.Firms;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Api.Tests.Firms;

[Collection(ApiCollection.Name)]
public class MemberEndpointsTests(PlumyApiFactory factory)
{
    private const string MembersUrl = "/api/firm/members";

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    /// <summary>A new user joins the owner's firm through an invitation.</summary>
    private async Task<(HttpClient Client, string Email)> AddMemberAsync(HttpClient owner, string name = "Luis Pérez")
    {
        var email = NewEmail();
        var invited = await owner.PostAsJsonAsync("/api/firm/invitations", new { email });
        var invitation = await invited.Content.ReadFromJsonAsync<InvitationResponse>();
        var member = factory.CreateClientWithoutFirm(email, name);
        (await member.PostAsync($"/api/invitations/{invitation!.Id}/accept", null)).EnsureSuccessStatusCode();
        return (member, email);
    }

    private static async Task<List<MemberResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<MemberResponse>>(MembersUrl))!;

    [Fact]
    public async Task EveryoneInTheFirm_SeesItsPeople_WithThemselvesMarked()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var (member, memberEmail) = await AddMemberAsync(owner);

        var asOwner = await ListAsync(owner);
        var asMember = await ListAsync(member);

        Assert.Equal(
            [("Test User", "test.user@example.com", "Owner", true), ("Luis Pérez", memberEmail, "Member", false)],
            asOwner.Select(m => (m.Name, m.Email, m.Role, m.IsCurrentUser)));
        Assert.Equal([false, true], asMember.Select(m => m.IsCurrentUser));
        Assert.Equal(asOwner.Select(m => m.Id), asMember.Select(m => m.Id));
    }

    [Fact]
    public async Task Owner_RemovesAMember_WhoLosesAccessToTheFirm()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var (member, _) = await AddMemberAsync(owner);
        var memberId = (await ListAsync(owner))[1].Id;

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{MembersUrl}/{memberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{MembersUrl}/{memberId}")).StatusCode);

        Assert.Single(await ListAsync(owner));
        Assert.Null((await member.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.Firm);
        var denied = await member.GetAsync("/api/client-companies");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("urn:plumy:problem:no-firm", problem?.Type);
    }

    [Fact]
    public async Task Owner_CannotRemoveThemselves()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var ownerId = (await ListAsync(owner)).Single().Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"{MembersUrl}/{ownerId}")).StatusCode);
        Assert.Single(await ListAsync(owner));
    }

    [Fact]
    public async Task Member_CannotRemoveAnyone()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var (member, _) = await AddMemberAsync(owner);
        await AddMemberAsync(owner, "Marta Ruiz");
        var people = await ListAsync(owner);

        foreach (var person in people)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"{MembersUrl}/{person.Id}")).StatusCode);
        }

        Assert.Equal(3, (await ListAsync(owner)).Count);
    }

    [Fact]
    public async Task Owner_CannotSeeNorRemoveAnotherFirmsPeople()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        await AddMemberAsync(owner);
        var otherOwner = await factory.CreateClientInNewFirmAsync("Otra gestoría");

        Assert.Single(await ListAsync(otherOwner));
        foreach (var person in await ListAsync(owner))
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await otherOwner.DeleteAsync($"{MembersUrl}/{person.Id}")).StatusCode);
        }

        Assert.Equal(2, (await ListAsync(owner)).Count);
    }

    [Fact]
    public async Task Me_KeepsTheNameTheTeamSeesUpToDate()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var email = NewEmail();
        var invitation = await (await owner.PostAsJsonAsync("/api/firm/invitations", new { email }))
            .Content.ReadFromJsonAsync<InvitationResponse>();
        var objectId = Guid.NewGuid();
        var before = factory.CreateClient(TestTokens.Create(factory.ValidToken() with
        {
            ObjectId = objectId, Email = email, Name = "Luis"
        }));
        (await before.PostAsync($"/api/invitations/{invitation!.Id}/accept", null)).EnsureSuccessStatusCode();

        // Same account, renamed in Entra: the next token carries the new name
        var after = factory.CreateClient(TestTokens.Create(factory.ValidToken() with
        {
            ObjectId = objectId, Email = email, Name = "Luis Pérez Gil"
        }));
        await after.GetFromJsonAsync<CurrentUserResponse>("/api/me");

        Assert.Equal("Luis Pérez Gil", (await ListAsync(owner))[1].Name);
    }

    [Fact]
    public async Task UserWithoutFirm_Gets403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientWithoutFirm().GetAsync(MembersUrl)).StatusCode);
    }

    [Fact]
    public async Task MemberEndpoints_WithoutToken_Return401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(MembersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"{MembersUrl}/{Guid.NewGuid()}")).StatusCode);
    }
}
