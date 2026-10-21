using System.Net;
using System.Net.Http.Json;
using Plumy.Api.Tests.Infrastructure;
using Plumy.Application.ClientCompanies;
using Plumy.Application.Common;
using Plumy.Application.Firms;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Api.Tests.Firms;

[Collection(ApiCollection.Name)]
public class InvitationEndpointsTests(PlumyApiFactory factory)
{
    private const string InvitationsUrl = "/api/firm/invitations";

    // Every test invites its own address: the database is shared by the whole collection
    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    private static string AcceptUrl(Guid id) => $"/api/invitations/{id}/accept";

    [Fact]
    public async Task InvitedUser_SeesTheInvitation_AcceptsIt_AndWorksWithTheFirmsData()
    {
        var owner = await factory.CreateClientInNewFirmAsync("Gestoría Invitadora");
        var company = await (await owner.PostAsJsonAsync("/api/client-companies",
            new { name = "Cliente común S.L.", taxId = TaxIds.NewNif() })).Content.ReadFromJsonAsync<ClientCompanyResponse>();
        var email = NewEmail();

        var invited = await owner.PostAsJsonAsync(InvitationsUrl, new { email = email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var invitation = await invited.Content.ReadFromJsonAsync<InvitationResponse>();
        Assert.NotNull(invitation);
        Assert.Equal(email, invitation.Email);
        Assert.False(invitation.IsExpired);
        Assert.Equal([invitation], await owner.GetFromJsonAsync<List<InvitationResponse>>(InvitationsUrl));

        var invitee = factory.CreateClientWithoutFirm(email);
        var pending = Assert.Single((await invitee.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.Invitations);
        Assert.Equal(new PendingInvitation(invitation.Id, "Gestoría Invitadora", invitation.ExpiresAt), pending);

        var accepted = await invitee.PostAsync(AcceptUrl(invitation.Id), null);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var membership = await accepted.Content.ReadFromJsonAsync<FirmMembershipResponse>();
        Assert.Equal("Gestoría Invitadora", membership?.Name);
        Assert.Equal("Member", membership?.Role);

        var me = await invitee.GetFromJsonAsync<CurrentUserResponse>("/api/me");
        Assert.Equal(membership, me?.Firm);
        Assert.Empty(me!.Invitations);

        // The point of it all: the new member works on the same client companies as the owner
        var companies = await invitee.GetFromJsonAsync<PagedResult<ClientCompanyResponse>>("/api/client-companies");
        Assert.Equal([company!.Id], companies!.Items.Select(c => c.Id));

        // Accepted invitations leave the owner's list
        Assert.Empty((await owner.GetFromJsonAsync<List<InvitationResponse>>(InvitationsUrl))!);
    }

    [Fact]
    public async Task Invite_SameAddressTwice_RenewsTheSameInvitation()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var email = NewEmail();

        var first = await (await owner.PostAsJsonAsync(InvitationsUrl, new { email }))
            .Content.ReadFromJsonAsync<InvitationResponse>();
        var second = await (await owner.PostAsJsonAsync(InvitationsUrl, new { email }))
            .Content.ReadFromJsonAsync<InvitationResponse>();

        Assert.Equal(first!.Id, second!.Id);
        Assert.Single((await owner.GetFromJsonAsync<List<InvitationResponse>>(InvitationsUrl))!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no es un email")]
    [InlineData("test.user@example.com")] // the owner's own address
    public async Task Invite_InvalidAddress_Returns400(string? email)
    {
        var owner = await factory.CreateClientInNewFirmAsync();

        var response = await owner.PostAsJsonAsync(InvitationsUrl, new { email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_ThenTheInviteeNoLongerSeesNorCanAcceptIt()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var email = NewEmail();
        var invitation = await InviteAsync(owner, email);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{InvitationsUrl}/{invitation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{InvitationsUrl}/{invitation.Id}")).StatusCode);

        var invitee = factory.CreateClientWithoutFirm(email);
        Assert.Empty((await invitee.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.Invitations);
        Assert.Equal(HttpStatusCode.NotFound, (await invitee.PostAsync(AcceptUrl(invitation.Id), null)).StatusCode);
    }

    [Fact]
    public async Task Accept_InvitationSentToSomeoneElse_Returns404AndJoinsNothing()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var invitation = await InviteAsync(owner, NewEmail());
        var intruder = factory.CreateClientWithoutFirm(NewEmail());

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync(AcceptUrl(invitation.Id), null)).StatusCode);

        Assert.Null((await intruder.GetFromJsonAsync<CurrentUserResponse>("/api/me"))!.Firm);
    }

    [Fact]
    public async Task Accept_UserWithoutEmailClaim_Returns400()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var invitation = await InviteAsync(owner, NewEmail());
        var noEmail = factory.CreateClient(TestTokens.Create(factory.ValidToken() with
        {
            ObjectId = Guid.NewGuid(), Email = null
        }));

        Assert.Equal(HttpStatusCode.BadRequest, (await noEmail.PostAsync(AcceptUrl(invitation.Id), null)).StatusCode);
    }

    [Fact]
    public async Task Accept_UserAlreadyInAFirm_Returns409()
    {
        var email = NewEmail();
        var alreadyOwner = factory.CreateClientWithoutFirm(email);
        (await alreadyOwner.PostAsJsonAsync("/api/firms", new { name = "Su propia gestoría" })).EnsureSuccessStatusCode();
        var invitation = await InviteAsync(await factory.CreateClientInNewFirmAsync(), email);

        var response = await alreadyOwner.PostAsync(AcceptUrl(invitation.Id), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Member_CannotManageInvitations()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var email = NewEmail();
        var member = factory.CreateClientWithoutFirm(email);
        (await member.PostAsync(AcceptUrl((await InviteAsync(owner, email)).Id), null)).EnsureSuccessStatusCode();
        var pending = await InviteAsync(owner, NewEmail());

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(InvitationsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync(InvitationsUrl, new { email = NewEmail() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"{InvitationsUrl}/{pending.Id}")).StatusCode);

        Assert.Single((await owner.GetFromJsonAsync<List<InvitationResponse>>(InvitationsUrl))!);
    }

    [Fact]
    public async Task UserWithoutFirm_CannotManageInvitations()
    {
        var client = factory.CreateClientWithoutFirm();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(InvitationsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(InvitationsUrl, new { email = NewEmail() })).StatusCode);
    }

    [Fact]
    public async Task Owner_CannotSeeNorRevokeAnotherFirmsInvitations()
    {
        var owner = await factory.CreateClientInNewFirmAsync();
        var invitation = await InviteAsync(owner, NewEmail());
        var otherOwner = await factory.CreateClientInNewFirmAsync("Otra gestoría");

        Assert.Empty((await otherOwner.GetFromJsonAsync<List<InvitationResponse>>(InvitationsUrl))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherOwner.DeleteAsync($"{InvitationsUrl}/{invitation.Id}")).StatusCode);

        Assert.Single((await owner.GetFromJsonAsync<List<InvitationResponse>>(InvitationsUrl))!);
    }

    [Fact]
    public async Task InvitationEndpoints_WithoutToken_Return401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(InvitationsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(InvitationsUrl, new { email = NewEmail() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(AcceptUrl(Guid.NewGuid()), null)).StatusCode);
    }

    private static async Task<InvitationResponse> InviteAsync(HttpClient owner, string email)
    {
        var response = await owner.PostAsJsonAsync(InvitationsUrl, new { email });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvitationResponse>())!;
    }
}
